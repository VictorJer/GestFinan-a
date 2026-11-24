using Finora.Application.FinancialData;
using Finora.Domain.FinancialData;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Confirma isolamento e cardinalidade da consulta usando somente o PostgreSQL descartável validado.
[DoNotParallelize]
[TestClass]
public sealed class PostgreSqlCurrentCardsReaderTests
{
    private static readonly Guid OwnerId = Guid.Parse("2805d9bc-5a86-44e3-9d55-b98a6c5d3ebf");
    private static readonly Guid OtherUserId = Guid.Parse("f8c66351-5a2a-41d4-a47c-daa23918b659");
    private static readonly Guid OwnerConnectionId = Guid.Parse("35c956ec-ed74-4aeb-9229-65f4fd80791b");
    private static readonly Guid OtherConnectionId = Guid.Parse("fe67978b-e4dc-4dbe-b0a2-555a5774aefc");
    private static readonly Guid OwnerCardId = Guid.Parse("cc1df1eb-4ff2-45e7-9c8d-749ac5dbf931");
    private static readonly Guid SecondOwnerCardId = Guid.Parse("7275a9e8-8791-4b44-9f92-4b3001f4c441");
    private static readonly Guid OtherCardId = Guid.Parse("59d95513-2a61-4486-babb-69a3b981d9fa");

    // O reader retorna somente cartões da conexão única do titular, mesmo havendo outro usuário com dados próprios.
    [TestMethod]
    public async Task ReaderReturnsCardsOnlyFromTheRequestedUsersUniqueConnection()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        await SeedTwoUsersAsync(fixture.Context);

        var result = await new CurrentCardsReader(fixture.Context).ReadAsync(OwnerId, CancellationToken.None);

        Assert.AreEqual(CurrentCardsQueryStatus.Found, result.Status);
        Assert.IsNotNull(result.Cards);
        CollectionAssert.AreEquivalent(new[] { OwnerCardId, SecondOwnerCardId }, result.Cards.Select(card => card.Id).ToArray());
        var ownerCard = result.Cards.Single(card => card.Id == OwnerCardId);
        Assert.AreEqual("Conta principal", ownerCard.Name);
        Assert.AreEqual("1234", ownerCard.LastFour);
        Assert.AreEqual("BRL", ownerCard.CurrencyCode);
        Assert.IsFalse(result.Cards.Any(card => card.Id == OtherCardId));
    }

    // Ausência e ambiguidade não se confundem com lista vazia nem causam seleção arbitrária de conexão.
    [TestMethod]
    public async Task ReaderDistinguishesMissingAndAmbiguousConnections()
    {
        await using var fixture = DisposablePostgresMigrationFixture.CreateValidated();
        await fixture.ResetToEmptySchemaAsync();
        await fixture.Context.Database.MigrateAsync(PostgreSqlPersistenceTests.CoreMigrationId);
        fixture.Context.Users.Add(new ApplicationUser(OwnerId));
        await fixture.Context.SaveChangesAsync();

        var reader = new CurrentCardsReader(fixture.Context);
        var missing = await reader.ReadAsync(OwnerId, CancellationToken.None);
        Assert.AreEqual(CurrentCardsQueryStatus.ConnectionNotFound, missing.Status);
        Assert.IsNull(missing.Cards);

        fixture.Context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(OwnerConnectionId, OwnerId, "pluggy", "owner-item-one"),
            FinancialConnection.Create(Guid.Parse("16f2e192-9be8-4c88-9b70-559a92781b03"), OwnerId, "pluggy", "owner-item-two"));
        await fixture.Context.SaveChangesAsync();

        var ambiguous = await reader.ReadAsync(OwnerId, CancellationToken.None);
        Assert.AreEqual(CurrentCardsQueryStatus.ConnectionAmbiguous, ambiguous.Status);
        Assert.IsNull(ambiguous.Cards);
    }

    // Insere dados sintéticos de dois titulares para verificar os predicados de titularidade no PostgreSQL.
    private static async Task SeedTwoUsersAsync(FinoraDbContext context)
    {
        context.Users.AddRange(new ApplicationUser(OwnerId), new ApplicationUser(OtherUserId));
        await context.SaveChangesAsync();
        context.Set<FinancialConnection>().AddRange(
            FinancialConnection.Create(OwnerConnectionId, OwnerId, "pluggy", "owner-item"),
            FinancialConnection.Create(OtherConnectionId, OtherUserId, "pluggy", "other-item"));
        await context.SaveChangesAsync();
        context.Set<CardAccount>().AddRange(
            CardAccount.Create(OwnerCardId, OwnerId, OwnerConnectionId, "owner-account-one", "BRL", "Conta principal", "1234"),
            CardAccount.Create(SecondOwnerCardId, OwnerId, OwnerConnectionId, "owner-account-two", "USD", "Conta internacional"),
            CardAccount.Create(OtherCardId, OtherUserId, OtherConnectionId, "other-account", "EUR", "Conta alheia", "9999"));
        await context.SaveChangesAsync();
    }
}
