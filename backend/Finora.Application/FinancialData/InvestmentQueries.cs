using Finora.Domain.FinancialData;
using MediatR;

namespace Finora.Application.FinancialData;

// Cada grupo apresenta somente valor atual por classe, sem percentuais ou inferência de rentabilidade ajustada.
public sealed record InvestmentClassValue(string Class, string Label, decimal CurrentAmount);

// O resumo deriva todos os campos do mesmo conjunto de itens materializado pela consulta do titular.
public sealed record InvestmentPortfolioSummary(
    decimal TotalAppliedAmount,
    decimal TotalCurrentAmount,
    decimal Difference,
    IReadOnlyList<InvestmentClassValue> CurrentAmountsByClass);

// Lista e resumo são retornados juntos para impedir que a interface concilie fontes ou leituras diferentes.
public sealed record InvestmentPortfolioResult(
    IReadOnlyList<InvestmentItem> Items,
    InvestmentPortfolioSummary Summary);

// O identificador é interno e deverá vir da sessão validada na borda HTTP, nunca de filtro do cliente.
public sealed record GetInvestmentPortfolioQuery(Guid UserId) : IRequest<InvestmentPortfolioResult>;

// A query depende exclusivamente do store manual e, portanto, não pode agregar movimentos de cartão.
public sealed class GetInvestmentPortfolioHandler(IInvestmentStore store)
    : IRequestHandler<GetInvestmentPortfolioQuery, InvestmentPortfolioResult>
{
    public async Task<InvestmentPortfolioResult> Handle(
        GetInvestmentPortfolioQuery request,
        CancellationToken cancellationToken)
    {
        // Um identificador vazio não pode corresponder a titular válido e não dispara acesso ao adapter.
        var persistedItems = request.UserId == Guid.Empty
            ? Array.Empty<InvestmentItem>()
            : await store.ReadOwnedAsync(request.UserId, cancellationToken);

        // Ordenação ordinal por nome e depois UUID deixa a resposta estável em qualquer cultura de execução.
        var items = persistedItems
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Id)
            .ToArray();

        // Lista e resumo usam exatamente a mesma materialização; classes sem posição são explicitamente zero.
        var summary = Summarize(items);
        return new InvestmentPortfolioResult(items, summary);
    }

    // Soma valores manuais e mantém a diferença como atual menos aplicado, que pode ser negativa.
    private static InvestmentPortfolioSummary Summarize(IReadOnlyList<InvestmentItem> items)
    {
        var totalApplied = 0m;
        var totalCurrent = 0m;
        var currentByClass = InvestmentClassCatalog.Options.ToDictionary(
            option => option.Code,
            _ => 0m,
            StringComparer.Ordinal);

        foreach (var item in items)
        {
            totalApplied += item.AppliedAmount;
            totalCurrent += item.CurrentAmount;
            if (!currentByClass.TryGetValue(item.Class, out var currentAmount))
            {
                throw new InvalidOperationException("A carteira persistida contém uma classe não permitida.");
            }

            currentByClass[item.Class] = currentAmount + item.CurrentAmount;
        }

        // A ausência de percentual evita divisão por zero quando a carteira está vazia ou vale zero.
        var classValues = InvestmentClassCatalog.Options
            .Select(option => new InvestmentClassValue(
                option.Code,
                option.Label,
                currentByClass[option.Code]))
            .ToArray();

        return new InvestmentPortfolioSummary(
            totalApplied,
            totalCurrent,
            totalCurrent - totalApplied,
            classValues);
    }
}
