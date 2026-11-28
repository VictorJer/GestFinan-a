using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Finora.Api.Authentication;
using Finora.Application.Authentication;
using Finora.Application.FinancialData;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Persistence;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Finora.Api.Tests;

// Valida a fronteira HTTP sem PostgreSQL: a rota de cálculo não depende de persistência nem de carteira.
[DoNotParallelize]
[TestClass]
public sealed class InvestmentSimulationApiTests
{
    private const string TestScheme = "SimulationTest";
    private const string TestToken = "simulation-test-token";

    // A série de máximo permitido comprova os limites inclusivos e mantém todos os valores publicados textuais.
    [TestMethod]
    public async Task CalculateReturnsAllowlistedSeriesAtMaximumInputs()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await SendCalculationAsync(client,
            "{\"initialAmount\":\"10000000.00\",\"monthlyContribution\":\"1000000.00\",\"annualRatePercent\":\"50.00\",\"years\":30}",
            authenticated: true);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        CollectionAssert.AreEquivalent(new[] { "points", "totalContributed", "projectedValue", "projectedReturn" },
            body.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        var points = body.RootElement.GetProperty("points");
        Assert.AreEqual(361, points.GetArrayLength());
        Assert.AreEqual(0, points[0].GetProperty("month").GetInt32());
        Assert.AreEqual("10000000.00", points[0].GetProperty("totalContributed").GetString());
        Assert.AreEqual("10000000.00", points[0].GetProperty("projectedValue").GetString());
        Assert.AreEqual(360, points[360].GetProperty("month").GetInt32());
        Assert.AreEqual("370000000.00", body.RootElement.GetProperty("totalContributed").GetString());
        Assert.IsTrue(IsCanonicalAmount(body.RootElement.GetProperty("projectedValue").GetString()));
        Assert.IsTrue(IsCanonicalAmount(body.RootElement.GetProperty("projectedReturn").GetString()));
        Assert.IsFalse(body.RootElement.TryGetProperty("userId", out _));
        Assert.IsFalse(body.RootElement.TryGetProperty("portfolio", out _));
    }

    // Valores mínimos e taxa zero passam pelo mesmo parser e resultam em série válida sem inventar saldo.
    [TestMethod]
    public async Task CalculateAcceptsMinimumInputsAndZeroRate()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await SendCalculationAsync(client,
            "{\"initialAmount\":\"0.00\",\"monthlyContribution\":\"0.00\",\"annualRatePercent\":\"0.00\",\"years\":1}",
            authenticated: true);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual("0.00", body.RootElement.GetProperty("totalContributed").GetString());
        Assert.AreEqual("0.00", body.RootElement.GetProperty("projectedValue").GetString());
        Assert.AreEqual("0.00", body.RootElement.GetProperty("projectedReturn").GetString());
        Assert.AreEqual(13, body.RootElement.GetProperty("points").GetArrayLength());
    }

    // O contrato estrito recusa omissões, campos adicionais/duplicados, tipos errados e formas decimais não canônicas.
    [TestMethod]
    public async Task CalculateRejectsInvalidJsonShapesAndSanitizesResponses()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var invalidBodies = new[]
        {
            "{\"initialAmount\":\"1.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\"}",
            "{\"initialAmount\":\"1.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1,\"extra\":\"private-simulation-sentinel\"}",
            "{\"initialAmount\":\"1.00\",\"initialAmount\":\"2.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1}",
            "{\"initialAmount\":1.00,\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1}",
            "{\"initialAmount\":\"01.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1}",
            "{\"initialAmount\":\"1.0\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1}",
            "{\"initialAmount\":\"-1.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1}",
            "{\"initialAmount\":\"10000000.01\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1}",
            "{\"initialAmount\":\"1.00\",\"monthlyContribution\":\"1000000.01\",\"annualRatePercent\":\"1.00\",\"years\":1}",
            "{\"initialAmount\":\"1.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"50.01\",\"years\":1}",
            "{\"initialAmount\":\"1.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":1.0}",
            "{\"initialAmount\":\"1.00\",\"monthlyContribution\":\"1.00\",\"annualRatePercent\":\"1.00\",\"years\":31}"
        };

        foreach (var invalidBody in invalidBodies)
        {
            using var response = await SendCalculationAsync(client, invalidBody, authenticated: true);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, invalidBody);
            Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains("private-simulation-sentinel", StringComparison.Ordinal));
            Assert.IsFalse(body.Contains("initialAmount", StringComparison.Ordinal));
            using var problem = JsonDocument.Parse(body);
            Assert.AreEqual("REQUEST_INVALID_INPUT", problem.RootElement.GetProperty("code").GetString());
        }
    }

    // Corpos excessivos ou JSON malformado não chegam ao calculador e recebem o envelope genérico da API.
    [TestMethod]
    public async Task CalculateRejectsMalformedAndOversizedBodies()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var malformed = await SendCalculationAsync(client, "{ invalid-json-sentinel", authenticated: true);
        Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.IsFalse((await malformed.Content.ReadAsStringAsync()).Contains("invalid-json-sentinel", StringComparison.Ordinal));

        using var oversized = await SendCalculationAsync(client, new string(' ', 4097), authenticated: true);
        Assert.AreEqual(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.AreEqual("application/problem+json", oversized.Content.Headers.ContentType?.MediaType);

        using var unknownLengthRequest = new HttpRequestMessage(HttpMethod.Post, "/api/simulations/calculate")
        {
            Content = new UnknownLengthSimulationContent(new string(' ', 4097))
        };
        unknownLengthRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestToken);
        using var unknownLength = await client.SendAsync(unknownLengthRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, unknownLength.StatusCode);
        Assert.AreEqual("application/problem+json", unknownLength.Content.Headers.ContentType?.MediaType);
    }

    // A autorização ocorre antes da ação, e o limitador global de titulares rejeita excesso com 429.
    [TestMethod]
    public async Task CalculateRequiresAuthenticationAndHonorsProtectedRateLimit()
    {
        await using (var anonymousFactory = CreateFactory())
        using (var anonymousClient = anonymousFactory.CreateClient())
        using (var anonymous = await SendCalculationAsync(anonymousClient, ValidRequest, authenticated: false))
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        await using var limitedFactory = CreateFactory(permitLimit: 1);
        using var limitedClient = limitedFactory.CreateClient();
        using var first = await SendCalculationAsync(limitedClient, ValidRequest, authenticated: true);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        using var excess = await SendCalculationAsync(limitedClient, ValidRequest, authenticated: true);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, excess.StatusCode);
        Assert.AreEqual("application/problem+json", excess.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await excess.Content.ReadAsStringAsync());
        Assert.AreEqual("RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
    }

    // Sem configuração, a rota continua documentada/protegida, falha fechada e não habilita handlers de Pluggy.
    [TestMethod]
    public async Task CalculateFailsClosedAndLeavesPluggyHandlersUnregisteredWithoutConfiguration()
    {
        await using var factory = new AuthenticationApiFactory();
        using var client = factory.CreateClient();
        using var openApi = await client.GetAsync("/openapi/v1.json");
        Assert.AreEqual(HttpStatusCode.OK, openApi.StatusCode);
        using (var document = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync()))
        {
            var operation = document.RootElement.GetProperty("paths")
                .GetProperty("/api/simulations/calculate").GetProperty("post");
            Assert.AreEqual("Bearer", operation.GetProperty("security")[0].EnumerateObject().Single().Name);
        }

        using var response = await client.PostAsync("/api/simulations/calculate",
            new StringContent(ValidRequest, Encoding.UTF8, "application/json"));
        await AuthenticationAvailabilityTests.AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable);

        using var scope = factory.Services.CreateScope();
        Assert.IsFalse(scope.ServiceProvider.GetRequiredService<AuthenticationAvailability>().IsAvailable);
        Assert.IsNull(scope.ServiceProvider.GetService<IRequestHandler<PluggyLocalReadQuery, PluggyLocalReadResult>>());
        Assert.IsNull(scope.ServiceProvider.GetService<IRequestHandler<PluggyAnalysisQuery, PluggyAnalysisResult>>());
        Assert.IsNull(scope.ServiceProvider.GetService<IRequestHandler<ImportTransactionsCommand, TransactionImportResult>>());
        Assert.IsNull(scope.ServiceProvider.GetService<FinoraDbContext>());
    }

    // A configuração válida mantém JWT real; somente a consulta persistente da sessão é substituída no teste.
    [TestMethod]
    public async Task CalculateRunsInValidAuthenticationCompositionWithoutDatabaseAccess()
    {
        var configuration = AuthenticationAvailabilityTests.Configuration();
        await using var factory = new AuthenticationApiFactory(configuration, configure: services =>
        {
            services.RemoveAll<IAuthenticationSessionReader>();
            services.AddSingleton<IAuthenticationSessionReader, ActiveSimulationSessionReader>();
        });
        using var client = factory.CreateClient();
        var token = CreateSignedTestToken(factory.Services);
        using var response = await SendCalculationWithTokenAsync(client, ValidRequest, token);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        using var scope = factory.Services.CreateScope();
        Assert.IsTrue(scope.ServiceProvider.GetRequiredService<AuthenticationAvailability>().IsAvailable);
        Assert.IsNull(scope.ServiceProvider.GetService<IRequestHandler<PluggyLocalReadQuery, PluggyLocalReadResult>>());
        Assert.IsNull(scope.ServiceProvider.GetService<IRequestHandler<PluggyAnalysisQuery, PluggyAnalysisResult>>());
        Assert.AreEqual(System.Data.ConnectionState.Closed,
            scope.ServiceProvider.GetRequiredService<FinoraDbContext>().Database.GetDbConnection().State);
    }

    private const string ValidRequest =
        "{\"initialAmount\":\"100.00\",\"monthlyContribution\":\"10.00\",\"annualRatePercent\":\"0.00\",\"years\":1}";

    // Configuração sem banco é intencional: o handler sintético permite testar apenas o limite protegido e a API.
    private static AuthenticationApiFactory CreateFactory(int permitLimit = 20) => new(
        new Dictionary<string, string?>
        {
            ["RateLimiting:Protected:PermitLimit"] = permitLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:Protected:WindowSeconds"] = "60"
        },
        configure: services => services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
                options.DefaultScheme = TestScheme;
            })
            .AddScheme<AuthenticationSchemeOptions, SimulationTestAuthenticationHandler>(TestScheme, _ => { }));

    // O teste envia texto JSON cru para preservar duplicatas e grafias que PostAsJsonAsync normalizaria.
    private static async Task<HttpResponseMessage> SendCalculationAsync(HttpClient client, string json, bool authenticated)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/simulations/calculate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestToken);
        }

        return await client.SendAsync(request);
    }

    // Emite JWT pela mesma implementação da API para testar a composição autenticada sem token artificial.
    private static string CreateSignedTestToken(IServiceProvider services)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return new JwtAccessTokenIssuer(services.GetRequiredService<JwtSettings>())
            .Issue(Guid.NewGuid(), AuthSession.GenerateId(), now, now.AddSeconds(1800)).AccessToken;
    }

    // O teste envia o corpo textual original e mantém recursos HTTP vivos até a resposta estar completa.
    private static async Task<HttpResponseMessage> SendCalculationWithTokenAsync(
        HttpClient client,
        string json,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/simulations/calculate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    // Confirma formato canônico textual sem converter valores financeiros para double no teste.
    private static bool IsCanonicalAmount(string? value) =>
        value is not null
        && decimal.TryParse(value, System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
        && string.Equals(value, parsed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    // O conteúdo chunked prova que o teto também é aplicado quando o cliente não informa Content-Length.
    private sealed class UnknownLengthSimulationContent : HttpContent
    {
        private readonly string _value;

        // O content type JSON permite chegar ao parser; somente o comprimento permanece desconhecido.
        internal UnknownLengthSimulationContent(string value)
        {
            _value = value;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        // O tipo JSON permanece explícito enquanto o cálculo do comprimento é deliberadamente omitido.
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        // Envia bytes UTF-8 sem pré-calcular tamanho, acionando o caminho limitado de leitura da API.
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(_value)).AsTask();
    }
}

// A configuração valida JWT real, mas o leitor sintético evita contato com a conexão PostgreSQL de teste.
internal sealed class ActiveSimulationSessionReader : IAuthenticationSessionReader
{
    // Claims assinadas são preservadas; somente o estado de sessão persistido é representado pelo fixture.
    public Task<bool> IsActiveAsync(AuthenticationSessionReference session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(true);
    }
}

// A credencial sintética existe somente neste host de testes e não substitui o esquema JWT da aplicação.
internal sealed class SimulationTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    // Claims mínimos permitem exercitar autorização e particionamento do limitador sem sessão persistida.
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorization)
            || !string.Equals(authorization.ToString(), "Bearer simulation-test-token", StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim("sub", "simulation-test-owner")], Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // Challenge de teste preserva o status e o content type sanitizados sem expor detalhes do token.
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        await Response.WriteAsJsonAsync(new { status = StatusCodes.Status401Unauthorized, code = "AUTH_INVALID_CREDENTIALS" });
    }
}
