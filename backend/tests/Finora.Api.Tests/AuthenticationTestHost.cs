using System.Collections.Concurrent;
using System.Security.Claims;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
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
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(_clock);
            services.AddSingleton<AuthenticationProbeCounter>();
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
public sealed class AuthenticationProbeController(AuthenticationProbeCounter counter) : ControllerBase
{
    // O retorno mínimo comprova a identidade vinda do token/sessão, sem dados financeiros ou senha.
    [HttpGet]
    public IActionResult Get()
    {
        counter.Increment();
        return Ok(new { subject = User.FindFirstValue("sub") });
    }
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
