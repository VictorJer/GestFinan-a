using System.Text.Json;
using Finora.Infrastructure.Pluggy;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Garante que o contrato de análise retenha apenas campos permitidos e preserve valores externos.
[TestClass]
public sealed class PluggyAnalysisContractTests
{
    // Campos opcionais podem faltar e enums novos continuam como texto para decisão conservadora.
    [TestMethod]
    public void DeserializesMissingOptionalsAndUnknownEnumValues()
    {
        const string json = """
            {"id":"opaque-id","accountId":"opaque-account","description":"Fixture","amount":12.3400,
             "currencyCode":"BRL","date":"2026-01-02","type":"NEW_EXTERNAL_TYPE",
             "status":"NEW_STATUS","operationType":"NEW_OPERATION",
             "creditCardMetadata":{"installmentNumber":2,"totalInstallments":3,
             "purchaseDate":"2025-12-02","billId":"fixture-bill","billPostDate":"2026-01-02",
             "paymentType":"INSTALLMENT","cardNumber":"private-sentinel"}}
            """;

        var dto = JsonSerializer.Deserialize<PluggyAnalysisTransactionResponse>(json);

        Assert.IsNotNull(dto);
        Assert.AreEqual(12.3400m, dto.Amount);
        Assert.AreEqual("NEW_EXTERNAL_TYPE", dto.Type);
        Assert.AreEqual(2, dto.CreditCardMetadata?.InstallmentNumber);
        Assert.IsTrue(dto.TryToContract(out var contract));
        Assert.IsNotNull(contract);
        Assert.AreEqual("2026-01-02", contract.OriginalDate);
        Assert.AreEqual("fixture-bill", contract.BillId);
    }

    // A ausência do objeto de metadados de cartão permanece opcional e não altera os campos-base.
    [TestMethod]
    public void DeserializesWithoutOptionalCreditCardMetadata()
    {
        var dto = JsonSerializer.Deserialize<PluggyAnalysisTransactionResponse>(ValidTransaction("12.34"));

        Assert.IsNotNull(dto);
        Assert.IsNull(dto.CreditCardMetadata);
        Assert.IsTrue(dto.TryToContract(out var contract));
        Assert.IsNotNull(contract);
        Assert.IsNull(contract.InstallmentNumber);
    }

    // A ausência de campos-base não pode ser convertida em zero ou texto artificial.
    [TestMethod]
    [DataRow("{\"accountId\":\"a\",\"description\":\"x\",\"amount\":1,\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\"}")]
    [DataRow("{\"id\":\"i\",\"accountId\":\"a\",\"description\":\"x\",\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\"}")]
    [DataRow("{\"id\":\"i\",\"accountId\":\"a\",\"description\":\"x\",\"amount\":1,\"date\":\"2026-01-01\"}")]
    [DataRow("{\"id\":\"i\",\"accountId\":\"a\",\"description\":\"x\",\"amount\":1,\"currencyCode\":\"BRL\"}")]
    public void RejectsMissingRequiredContractFields(string json)
    {
        var dto = JsonSerializer.Deserialize<PluggyAnalysisTransactionResponse>(json);

        Assert.IsNotNull(dto);
        Assert.IsFalse(dto.TryToContract(out var contract));
        Assert.IsNull(contract);
    }

    // Decimal permanece exato; valores fora do tipo suportado são rejeitados sem arredondamento.
    [TestMethod]
    public void PreservesDecimalPrecisionAndRejectsOverflow()
    {
        var exact = JsonSerializer.Deserialize<PluggyAnalysisTransactionResponse>(ValidTransaction("1234567890.1234"));
        Assert.IsNotNull(exact);
        Assert.AreEqual(1234567890.1234m, exact.Amount);

        var rejected = false;
        try
        {
            JsonSerializer.Deserialize<PluggyAnalysisTransactionResponse>(ValidTransaction("1e100"));
        }
        catch (JsonException)
        {
            rejected = true;
        }

        Assert.IsTrue(rejected);
    }

    // Campos pessoais não fazem parte do DTO permitido, mesmo se enviados pela origem.
    [TestMethod]
    public void DoesNotRetainUnapprovedSensitiveFields()
    {
        var dto = JsonSerializer.Deserialize<PluggyAnalysisTransactionResponse>(
            ValidTransaction("1.25").TrimEnd('}') +
            ",\"creditCardMetadata\":{\"cardNumber\":\"private-sentinel\"},\"paymentData\":{\"secret\":\"private-sentinel\"}}");

        Assert.IsNotNull(dto);
        var retainedProperties = typeof(PluggyAnalysisTransactionResponse).GetProperties()
            .Select(property => property.Name);
        CollectionAssert.DoesNotContain(retainedProperties.ToArray(), "CardNumber");
        CollectionAssert.DoesNotContain(retainedProperties.ToArray(), "PaymentData");
        Assert.IsFalse(JsonSerializer.Serialize(dto).Contains("private-sentinel", StringComparison.Ordinal));
    }

    // Fixture contém exclusivamente dados sintéticos sem identificadores reais.
    private static string ValidTransaction(string amount) =>
        $"{{\"id\":\"fixture-id\",\"accountId\":\"fixture-account\",\"description\":\"Fixture\",\"amount\":{amount},\"currencyCode\":\"BRL\",\"date\":\"2026-01-01\"}}";
}
