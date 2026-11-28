using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Prova os comandos da carteira e o isolamento por titular exclusivamente em PostgreSQL descartável.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlInvestmentCommandTests
{
    private static readonly Guid OwnerId = Guid.Parse("53d5ac79-f9ab-4921-a6a1-1aa4ee0753a8");
    private static readonly Guid OtherOwnerId = Guid.Parse("9be62c42-7687-4c02-a8c5-69a02e872aef");
    private static readonly Guid MissingInvestmentId = Guid.Parse("6b4d0111-31a9-4fc4-b8cd-650e7efba684");

    // Criação é relida por outro contexto; IDs alheios e ausentes compartilham NotFound sem alterar o registro.
    [TestMethod]
    public async Task CommandsPersistAfterNewReadAndHideForeignInvestments()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync();
        fixture.Context.Users.AddRange(new ApplicationUser(OwnerId), new ApplicationUser(OtherOwnerId));
        await fixture.Context.SaveChangesAsync();

        var store = new InvestmentStore(fixture.Context);
        var create = new CreateInvestmentHandler(store);
        var created = await create.Handle(new CreateInvestmentCommand(
            OwnerId,
            "Reserva manual sintética",
            InvestmentClassCatalog.FixedIncome,
            450.25m,
            475.75m), default);
        Assert.AreEqual(InvestmentOperationStatus.Created, created.Status);
        var investmentId = created.Item!.Id;

        // O novo contexto força leitura relacional independente da entidade ainda rastreada pelo comando create.
        await using (var rereadContext = CreateContext())
        {
            var persisted = await rereadContext.Set<Investment>().AsNoTracking().SingleAsync();
            Assert.AreEqual(OwnerId, persisted.UserId);
            Assert.AreEqual("Reserva manual sintética", persisted.Name);
            Assert.AreEqual(450.25m, persisted.AppliedAmount);
            Assert.AreEqual(475.75m, persisted.CurrentAmount);
        }

        var update = new UpdateInvestmentHandler(store);
        var foreignUpdate = await update.Handle(new UpdateInvestmentCommand(
            OtherOwnerId,
            investmentId,
            "Tentativa alheia",
            InvestmentClassCatalog.VariableIncome,
            1m,
            2m), default);
        var missingUpdate = await update.Handle(new UpdateInvestmentCommand(
            OtherOwnerId,
            MissingInvestmentId,
            "Tentativa ausente",
            InvestmentClassCatalog.VariableIncome,
            1m,
            2m), default);
        Assert.AreEqual(InvestmentOperationStatus.NotFound, foreignUpdate.Status);
        Assert.AreEqual(missingUpdate.Status, foreignUpdate.Status);

        // A atualização do titular altera valores e classe e volta a ser confirmada por uma leitura independente.
        var ownerUpdate = await update.Handle(new UpdateInvestmentCommand(
            OwnerId,
            investmentId,
            "Reserva revisada",
            InvestmentClassCatalog.VariableIncome,
            500m,
            390.50m), default);
        Assert.AreEqual(InvestmentOperationStatus.Updated, ownerUpdate.Status);
        await using (var updatedContext = CreateContext())
        {
            var persisted = await updatedContext.Set<Investment>().AsNoTracking().SingleAsync();
            Assert.AreEqual(OwnerId, persisted.UserId);
            Assert.AreEqual("Reserva revisada", persisted.Name);
            Assert.AreEqual(InvestmentClassCatalog.VariableIncome, persisted.Class);
            Assert.AreEqual(500m, persisted.AppliedAmount);
            Assert.AreEqual(390.50m, persisted.CurrentAmount);
        }

        // A remoção também oculta titular alheio e recurso inexistente, mas permite excluir o próprio registro.
        var delete = new DeleteInvestmentHandler(store);
        var foreignDelete = await delete.Handle(new DeleteInvestmentCommand(OtherOwnerId, investmentId), default);
        var missingDelete = await delete.Handle(new DeleteInvestmentCommand(OtherOwnerId, MissingInvestmentId), default);
        Assert.AreEqual(InvestmentOperationStatus.NotFound, foreignDelete.Status);
        Assert.AreEqual(missingDelete.Status, foreignDelete.Status);

        var ownerDelete = await delete.Handle(new DeleteInvestmentCommand(OwnerId, investmentId), default);
        Assert.AreEqual(InvestmentOperationStatus.Deleted, ownerDelete.Status);
        await using var afterDeleteContext = CreateContext();
        Assert.AreEqual(0, await afterDeleteContext.Set<Investment>().CountAsync());
    }

    // Cada releitura usa a connection string dedicada, cuja guarda rejeita qualquer database fora da allowlist.
    private static FinoraDbContext CreateContext()
    {
        var settings = DisposablePostgresTestConfiguration.ReadRequired();
        var options = new DbContextOptionsBuilder<FinoraDbContext>();
        FinoraDbContextConfiguration.Configure(options, settings.ConnectionString);
        return new FinoraDbContext(options.Options);
    }
}
