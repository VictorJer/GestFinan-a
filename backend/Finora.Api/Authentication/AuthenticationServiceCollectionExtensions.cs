using System.Text.Encodings.Web;
using Finora.Application.Authentication;
using Finora.Application.FinancialData;
using Finora.Api.Errors;
using Finora.Infrastructure.Identity;
using Finora.Infrastructure.Messaging;
using Finora.Infrastructure.Pluggy;
using Finora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using MassTransit;
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
        var rabbitMq = available ? SyncRunRabbitMqSettings.Read(configuration) : null;
        services.AddSingleton(new AuthenticationAvailability(available));
        services.TryAddSingleton(new SyncRunAcceptanceAvailability(false));
        services.AddMediatR(options =>
        {
            options.RegisterServicesFromAssembly(typeof(LoginCommand).Assembly);
            // A allowlist exclui CLI/Pluggy e handlers de banco só é habilitada com configuração autenticada válida.
            options.TypeEvaluator = type => type == typeof(LoginHandler)
                || (available && (type == typeof(LogoutHandler)
                    || type == typeof(AcceptSyncRunHandler)
                    || (rabbitMq is not null && type == typeof(ExecuteSyncRunHandler))
                     || type == typeof(ResolveCurrentSyncConnectionHandler)
                      || type == typeof(GetCurrentConnectionSyncStateHandler)
                      || type == typeof(GetCurrentCardsHandler)
                        || type == typeof(GetTransactionsQueryHandler)
                        || type == typeof(UpdateTransactionAnnotationHandler)
                        || type == typeof(CreatePersonalCategoryHandler)
                       || type == typeof(GetCategoriesHandler)
                      || type == typeof(GetSyncRunStatusHandler)));
        });
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = action =>
        {
            var problem = AuthenticationHttp.IsLogin(action.HttpContext)
                ? AuthenticationHttp.Problem(action.HttpContext, 400)
                : ApiProblemDetails.Create(action.HttpContext, 400);
            return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        });
        services.AddSingleton<ProblemDetailsFactory, ApiProblemDetailsFactory>();
        var authentication = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
        // Exige identidade por padrão; as rotas públicas precisam declarar a exceção explicitamente.
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationProblemResultHandler>();
        if (!available)
        {
            services.AddScoped<IAuthenticationLogin, UnavailableLogin>();
            authentication.AddScheme<AuthenticationSchemeOptions, UnavailableAuthenticationHandler>(JwtBearerDefaults.AuthenticationScheme, _ => { });
            return services;
        }

        // A política só participa da composição autenticada e valida seus limites antes de aceitar intenções.
        services.AddFinoraConnectionUpdatePolicyOptions();

        // Os serviços de banco são scoped; nenhuma conexão é aberta pela composição.
        services.AddSingleton(settings!);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddFinoraSessionPersistence(connection!);
        // Resolução, consulta de cartões e aceitação compartilham contexto scoped sem abrir conexão no startup.
        services.AddFinoraSyncRunAcceptancePersistence();
        if (rabbitMq is not null)
        {
            // Importação e consumer só existem quando a composição autenticada também dispõe de transporte explícito.
            services.AddPluggyTransactionImport(configuration);
            services.AddFinoraSyncRunExecutionOutbox(registration => registration.UsingRabbitMq((context, bus) =>
            {
                bus.Host(rabbitMq.HostAddress, host =>
                {
                    host.Username(rabbitMq.Username);
                    host.Password(rabbitMq.Password);
                    host.PublisherConfirmation = true;
                    if (rabbitMq.UseTls) { host.UseSsl(); }
                });
                // A fila classic processa uma única entrega por consumer; limites de ack serão tratados na aplicação.
                bus.ReceiveEndpoint(SyncRunExecutionTopology.QueueName, endpoint =>
                {
                    endpoint.PrefetchCount = SyncRunExecutionTopology.ConsumerPrefetchCount;
                    endpoint.ConcurrentMessageLimit = SyncRunExecutionTopology.ConsumerConcurrencyLimit;
                    endpoint.ConfigureConsumer<SyncRunExecutionConsumer>(context);
                });
                }));
            // A recuperação somente reenvia obrigações via outbox; o consumer continua como único executor financeiro.
            services.AddHostedService<SyncRunExecutionRecoveryService>();
            services.Replace(ServiceDescriptor.Singleton(new SyncRunAcceptanceAvailability(true)));
        }
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
