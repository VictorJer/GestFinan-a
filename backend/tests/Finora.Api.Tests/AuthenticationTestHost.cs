using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Finora.Api.Authentication;
using Finora.Application.Authentication;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Finora.Api.Tests;

// Este host usa a composição real e acrescenta a rota de prova exclusivamente ao assembly de testes.
internal sealed class AuthenticationApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _configuration;
    private readonly TimeProvider _clock;
    private readonly Action<IServiceCollection>? _configure;
    internal AuthenticationLogCapture Logs { get; } = new();

    // Valores nulos explícitos impedem herdar User Secrets/variáveis operacionais da máquina de desenvolvimento.
    internal AuthenticationApiFactory(Dictionary<string, string?>? configuration = null, TimeProvider? clock = null,
        Action<IServiceCollection>? configure = null)
    {
        _configuration = configuration ?? new Dictionary<string, string?>();
        foreach (var key in new[] { "ConnectionStrings:Finora", "Authentication:Jwt:Issuer", "Authentication:Jwt:Audience", "Authentication:Jwt:SigningKeyBase64" })
        {
            _configuration.TryAdd(key, null);
        }
        _clock = clock ?? TimeProvider.System;
        _configure = configure;
    }

    // Só a composição do host de testes descobre este Controller; binário e OpenAPI de produção não o incluem.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_configuration));
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(AuthenticationProbeController).Assembly);
            services.AddAuthorization(options => options.AddPolicy("test-forbidden",
                policy => policy.RequireClaim("permission", "not-granted")));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(_clock);
            services.AddSingleton<AuthenticationProbeCounter>();
            services.AddSingleton<AuthenticationProbeBarrier>();
            services.AddSingleton<OwnershipProbeStore>();
            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
            _configure?.Invoke(services);
        });
    }

    // Minimal hosting lê o snapshot antes de ConfigureServices; a fixture usa ambiente transitório, nunca argumentos com segredos.
    protected override IHost CreateHost(IHostBuilder builder)
    {
        var previous = new Dictionary<string, string?>();
        foreach (var entry in _configuration)
        {
            var variable = entry.Key.Replace(":", "__", StringComparison.Ordinal);
            previous[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, entry.Value);
        }
        try { return base.CreateHost(builder); }
        finally
        {
            foreach (var entry in previous) { Environment.SetEnvironmentVariable(entry.Key, entry.Value); }
        }
    }
}

// O estado mutável é restrito ao teste e permite provar que nenhum challenge executa a ação protegida.
public sealed class AuthenticationProbeCounter
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public void Increment() => Interlocked.Increment(ref _calls);
}

// Não existe no projeto Api, e sua exclusão do documento impede confundir prova com API de negócio entregue.
[ApiController]
[Authorize]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("_tests/authentication-probe")]
public sealed class AuthenticationProbeController(
    AuthenticationProbeCounter counter,
    AuthenticationProbeBarrier barrier) : ControllerBase
{
    // A barreira permite suspender somente a ação de teste depois da autenticação já concluída.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        counter.Increment();
        await barrier.WaitAsync(cancellationToken);
        return Ok(new { subject = User.FindFirstValue("sub") });
    }
}

// Prova a política fallback em um endpoint de teste sem [Authorize], fora do assembly de produção.
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("_tests/fallback-authentication-probe")]
public sealed class FallbackAuthenticationProbeController(
    AuthenticationProbeCounter counter,
    AuthenticationProbeBarrier barrier) : ControllerBase
{
    // A action mede o bypass do fallback e compartilha a barreira de concorrência das provas de sessão.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        counter.Increment();
        await barrier.WaitAsync(cancellationToken);
        return Ok(new { subject = User.FindFirstValue("sub") });
    }
}

// Mantém recursos sintéticos por titular para provar o predicado de propriedade sem criar API financeira real.
public sealed class OwnershipProbeStore
{
    private readonly object _gate = new();
    private (Guid Id, Guid OwnerId)[] _resources = [];
    private int _lookups;

    public int Lookups => Volatile.Read(ref _lookups);

    // A fixture fornece titulares diferentes e recursos associados, sem depender de dados externos.
    public void Seed(Guid ownerId, Guid otherOwnerId, Guid ownerResourceId, Guid otherResourceId)
    {
        lock (_gate)
        {
            _resources = [(ownerResourceId, ownerId), (otherResourceId, otherOwnerId)];
            Volatile.Write(ref _lookups, 0);
        }
    }

    // A consulta individual sempre combina o identificador solicitado com o titular autenticado.
    public Guid? FindOwned(Guid resourceId, Guid ownerId)
    {
        Interlocked.Increment(ref _lookups);
        lock (_gate)
        {
            return _resources.Where(resource => resource.Id == resourceId && resource.OwnerId == ownerId)
                .Select(resource => (Guid?)resource.Id).SingleOrDefault();
        }
    }

    // A listagem aceita somente o titular confiável; nenhum filtro de cliente participa do predicado.
    public Guid[] ListOwned(Guid ownerId)
    {
        Interlocked.Increment(ref _lookups);
        lock (_gate)
        {
            return _resources.Where(resource => resource.OwnerId == ownerId).Select(resource => resource.Id).ToArray();
        }
    }
}

// Demonstra no host de testes a identidade validada, o 404 indistinguível e o 403 sem consulta de recurso.
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("_tests/ownership-probe")]
public sealed class OwnershipProbeController(OwnershipProbeStore store, TimeProvider clock) : ControllerBase
{
    // O owner vem do principal e da sessão validada; a rota fornece somente o ID do recurso.
    [HttpGet("{resourceId:guid}")]
    public IActionResult Get(Guid resourceId)
    {
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null) { return ProblemResponse(401, "AUTH_INVALID_CREDENTIALS", "Autenticação inválida."); }
        var resource = store.FindOwned(resourceId, session.UserId);
        return resource is null
            ? ProblemResponse(404, "RESOURCE_NOT_FOUND", "Recurso não encontrado.")
            : Ok(new { id = resource.Value });
    }

    // Mesmo que o cliente envie outro ownerId, a listagem consulta exclusivamente o titular do principal.
    [HttpGet]
    public IActionResult List([FromQuery] Guid? ownerId)
    {
        var session = JwtTokenValidation.ReadSessionClaims(User, clock);
        if (session is null) { return ProblemResponse(401, "AUTH_INVALID_CREDENTIALS", "Autenticação inválida."); }
        return Ok(new { ids = store.ListOwned(session.UserId) });
    }

    // Permissão conhecida é avaliada sem consultar o ID, portanto 403 não confirma a existência do recurso.
    [HttpGet("restricted/{resourceId:guid}")]
    public IActionResult Restricted(Guid resourceId)
    {
        _ = resourceId;
        return User.HasClaim("permission", "read:restricted")
            ? Ok(new { permitted = true })
            : ProblemResponse(403, "PERMISSION_REQUIRED", "Permissão insuficiente.");
    }

    // Os probes usam um envelope fixo e traceId local, sem incluir titular ou identificador solicitado.
    private ObjectResult ProblemResponse(int status, string code, string title) => new(new ProblemDetails
    {
        Status = status,
        Title = title,
        Extensions = { ["code"] = code, ["traceId"] = HttpContext.TraceIdentifier }
    })
    { StatusCode = status, ContentTypes = { "application/problem+json" } };
}

// Fornece respostas vazias e validação MVC exclusivamente para exercitar o contrato HTTP centralizado.
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("_tests/problem-probe")]
public sealed class ProblemContractProbeController : ControllerBase
{
    // O model binder deve rejeitar o corpo sem refletir o valor sintético recebido.
    [HttpPost("validation")]
    public IActionResult Validate(ProblemValidationRequest request) => Ok(new { accepted = true });

    // Status sem corpo permite verificar a escrita comum de Problem Details pelo middleware.
    [HttpGet("conflict")]
    public IActionResult ConflictResult() => StatusCode(StatusCodes.Status409Conflict);
}

// Esta política só existe no host de testes e força forbid sem alterar políticas de produção.
[ApiController]
[Authorize(Policy = "test-forbidden")]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("_tests/forbidden-probe")]
public sealed class ForbiddenProblemProbeController : ControllerBase
{
    // A action só executa quando a policy de teste for satisfeita pelo principal.
    [HttpGet]
    public IActionResult Get() => Ok(new { permitted = true });
}

// Injeta falhas somente no host de testes para validar sanitização e limites de escrita do middleware global.
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("_tests/exception-probe")]
public sealed class ExceptionProbeController : ControllerBase
{
    // Mensagens sintéticas adversariais verificam que corpo e logs não recebem dados sensíveis externos.
    [HttpGet("unexpected")]
    public IActionResult Unexpected() => throw new InvalidOperationException(
        "password-sentinel token-sentinel SQL-sentinel provider-sentinel");

    // Somente a exceção operacional tipada permite distinguir dependência indisponível de falha inesperada.
    [HttpGet("unavailable")]
    public IActionResult Unavailable() => throw new AuthenticationUnavailableException();

    // O status sintético verifica cabeçalhos CORS em 429 sem alegar que o rate limiter já esteja conectado.
    [HttpGet("rate-limited")]
    public IActionResult RateLimited() => StatusCode(StatusCodes.Status429TooManyRequests);

    // A espera termina apenas com o cancelamento real do cliente e não produz uma resposta de erro inventada.
    [HttpGet("cancel")]
    public async Task<IActionResult> Cancel(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return Ok();
    }

    // A escrita é descarregada antes da falha para comprovar que o middleware não reescreve resposta iniciada.
    [HttpGet("started")]
    public async Task<IActionResult> Started()
    {
        Response.ContentType = "text/plain";
        await Response.WriteAsync("partial-response");
        await Response.Body.FlushAsync();
        throw new InvalidOperationException("password-sentinel token-sentinel SQL-sentinel provider-sentinel");
    }
}

// Um campo obrigatório provoca a fábrica MVC sem incluir erros de validação no corpo público.
public sealed class ProblemValidationRequest
{
    [Required]
    public string? Value { get; init; }
}

// Suspende uma chamada autenticada no Controller de teste para demonstrar que revogação não cancela trabalho em curso.
public sealed class AuthenticationProbeBarrier
{
    private TaskCompletionSource? _entered;
    private TaskCompletionSource? _release;
    private int _armed;

    // Cria uma barreira one-shot sem inserir espera/sleep nos fluxos normais das demais provas.
    internal Task Arm()
    {
        _entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref _armed, 1);
        return _entered.Task;
    }

    // Sinaliza entrada no Controller apenas depois de Authorization ter aceitado a sessão persistida.
    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _armed) == 0) { return; }
        _entered!.TrySetResult();
        await _release!.Task.WaitAsync(cancellationToken);
        Volatile.Write(ref _armed, 0);
    }

    // Desbloqueia também em finally para que uma asserção falha não deixe a fábrica HTTP presa.
    internal void Release() => _release?.TrySetResult();
}

// Controla fronteiras temporais em testes sem sleeps de 30 minutos ou alteração do relógio da máquina.
internal sealed class AuthenticationTestClock(DateTimeOffset initial) : TimeProvider
{
    private long _ticks = initial.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    internal void Set(DateTimeOffset value) => Interlocked.Exchange(ref _ticks, value.UtcTicks);
}

// Captura também exceções e scopes para detectar vazamento em providers de infraestrutura, não apenas mensagens fixas.
internal sealed class AuthenticationLogCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<string> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    internal string Text => string.Join('\n', _entries);
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
    public void Dispose() { }

    // Não filtra no capturador: os filtros devem vir da composição real da aplicação.
    private sealed class CaptureLogger(AuthenticationLogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner._scopes.Push(state);
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            owner._entries.Enqueue(category + ": " + formatter(state, exception) + exception);
            owner._scopes.ForEachScope((scope, entries) => entries.Enqueue(scope?.ToString() ?? ""), owner._entries);
        }
    }
}
