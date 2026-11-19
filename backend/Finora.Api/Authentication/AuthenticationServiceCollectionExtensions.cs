using System.Text.Encodings.Web;
using Finora.Application.Authentication;
using Finora.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;

namespace Finora.Api.Authentication;

// Estado mínimo não contém os valores externos que levaram à decisão de indisponibilidade.
public sealed record AuthenticationAvailability(bool IsAvailable);

/// <summary>Isola a disponibilidade de autenticação da liveness e dos comandos administrativos.</summary>
public static class AuthenticationServiceCollectionExtensions
{
    // A ausência de configuração não cria chave efêmera, banco fictício ou falha de ValidateOnStart.
    public static IServiceCollection AddFinoraHttpAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        var settings = JwtSettings.Read(configuration);
        var connection = configuration.GetConnectionString("Finora");
        var available = settings is not null && IsValidConnection(connection);
        services.AddSingleton(new AuthenticationAvailability(available));
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly);
            // Não registrar handlers CLI/Pluggy no HTTP só por compartilharem o assembly da Application.
            options.TypeEvaluator = type => type == typeof(LoginHandler)
                || (available && type == typeof(LogoutHandler));
        });
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = action =>
            new BadRequestObjectResult(AuthenticationHttp.Problem(action.HttpContext, 400))
            { ContentTypes = { "application/problem+json" } });
        var authentication = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
        services.AddAuthorization();
        if (!available)
        {
            services.AddScoped<IAuthenticationLogin, UnavailableLogin>();
            authentication.AddScheme<AuthenticationSchemeOptions, UnavailableAuthenticationHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { });
            return services;
        }

        // Os serviços de banco são scoped; nenhuma conexão é aberta pela composição.
        services.AddSingleton(settings!);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddFinoraSessionPersistence(connection!);
        authentication.AddJwtBearer((Action<JwtBearerOptions>)(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.Events = new JwtBearerEvents
            {
                // Ignora Bearer em endpoints anônimos, inclusive health/documentação e no próprio login.
                OnMessageReceived = context =>
                {
                    if (!AuthenticationHttp.IsProtected(context.HttpContext)) { context.NoResult(); }
                    return Task.CompletedTask;
                },
                // Não estabelece principal confiável sem semântica de claims e sessão confirmadas pelo contrato scoped.
                OnTokenValidated = async context =>
                {
                    var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                    var session = context.SecurityToken is JsonWebToken jwt ? JwtTokenValidation.ReadSession(jwt, clock) : null;
                    if (session is null) { context.Fail("Invalid authentication."); return; }
                    try
                    {
                        var reader = context.HttpContext.RequestServices.GetRequiredService<IAuthenticationSessionReader>();
                        if (!await reader.IsActiveAsync(session, context.HttpContext.RequestAborted)) { context.Fail("Invalid authentication."); }
                    }
                    catch (AuthenticationUnavailableException)
                    {
                        context.HttpContext.Items[AuthenticationHttp.UnavailableKey] = true;
                        context.Fail("Authentication unavailable.");
                    }
                },
                // Uma falha operacional permanece 503; somente falha de autenticação recebe challenge Bearer.
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    var status = context.HttpContext.Items.ContainsKey(AuthenticationHttp.UnavailableKey) ? 503 : 401;
                    if (status == 401) { context.Response.Headers.WWWAuthenticate = "Bearer"; }
                    return AuthenticationHttp.WriteProblemAsync(context.HttpContext, status);
                }
            };
        }));
        // O relógio pode ser substituído pelo host de testes sem congelá-lo durante o registro inicial.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((options, clock) => options.TokenValidationParameters = JwtTokenValidation.Parameters(settings!, clock));
        return services;
    }

    // O parser valida sintaxe/destino sem conectar, sem fallback para outra fonte ou registro da string recebida.
    private static bool IsValidConnection(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) { return false; }
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(connection);
            return !string.IsNullOrWhiteSpace(parsed.Host) && !string.IsNullOrWhiteSpace(parsed.Database)
                && !string.IsNullOrWhiteSpace(parsed.Username);
        }
        catch (ArgumentException) { return false; }
    }
}

// Sem configuração válida não se tenta resolver infraestrutura ou classificar senha como incorreta.
internal sealed class UnavailableLogin : IAuthenticationLogin
{
    public Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LoginResult.Unavailable());
    }
}

// Mantém falha fechada na ausência de chaves/configuração, mas não impede endpoints anônimos.
internal sealed class UnavailableAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => AuthenticationHttp.WriteProblemAsync(Context, 503);
}
