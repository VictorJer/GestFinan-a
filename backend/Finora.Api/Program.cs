using Finora.Api;
using Finora.Api.AdministrativeConnections;
using Finora.Api.Authentication;
using Finora.Api.Cors;
using Finora.Api.Errors;
using Finora.Api.OpenApi;
using Finora.Api.RateLimiting;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using MediatR;

// Qualquer argumento de processo é tratado como comando, exceto a assinatura fixa do host de testes.
if (args.Length > 0 && !PluggyReadCommand.IsApiTestHostInvocation(args))
{
    // O fluxo administrativo valida seus próprios tokens e compõe somente configuração/Identity.
    if (AdministrativeUserCommand.IsAdministrativeCommand(args))
    {
        return await AdministrativeUserCommand.RunAsync(args, Console.Out, Console.Error);
    }

    // O comando de vínculo é separado dos comandos Pluggy de leitura e retorna antes da composição HTTP.
    if (ConnectionLinkLocalCommand.IsAdministrativeCommand(args))
    {
        return await ConnectionLinkLocalCommand.RunAsync(args, Console.Error);
    }

    var isAnalysisCommand = string.Equals(args[0], "pluggy-analyze", StringComparison.Ordinal);
    if (isAnalysisCommand && !PluggyAnalyzeCommand.HasValidArguments(args))
    {
        return await PluggyAnalyzeCommand.RejectArgumentsAsync(args, Console.Error);
    }

    if (!isAnalysisCommand && !PluggyReadCommand.HasValidArguments(args))
    {
        return await PluggyReadCommand.RejectArgumentsAsync(args, Console.Error);
    }

    var commandBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        EnvironmentName = Environments.Development
    });
    commandBuilder.Services.AddMediatR(configuration =>
        configuration.RegisterServicesFromAssembly(typeof(PluggyLocalReadQuery).Assembly));
    commandBuilder.Services.AddPluggyApiClient(commandBuilder.Configuration);
    using var commandHost = commandBuilder.Build();
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;
    try
    {
        return isAnalysisCommand
            ? await PluggyAnalyzeCommand.RunAsync(args, commandHost.Services, Console.Out, cancellation.Token)
            : await PluggyReadCommand.RunAsync(args, commandHost.Services, Console.Out, cancellation.Token);
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
}

// O modo HTTP preserva o diagnóstico atual e não exige configuração Pluggy.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddFinoraHttpAuthentication(builder.Configuration);
builder.Services.AddFinoraCors(builder.Configuration);
builder.Services.AddFinoraRateLimiting(builder.Configuration);
// Providers de infraestrutura podem registrar SQL/PII em falhas; autenticação usa somente saídas fixas próprias.
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Npgsql", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Identity", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.None);
builder.Logging.AddFilter("Microsoft.IdentityModel", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Mvc", LogLevel.None);
// O gerador nativo preserva o transformer PATCH e acrescenta o esquema de autenticação ao documento.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeOpenApiDocumentTransformer>();
    options.AddOperationTransformer<TransactionAnnotationOpenApiTransformer>();
    options.AddOperationTransformer<TransactionRecurrenceDecisionOpenApiTransformer>();
    options.AddOperationTransformer<BearerSecurityOperationOpenApiTransformer>();
});

// Publica somente os Controllers implementados; diagnóstico não implica prontidão de integrações.
var app = builder.Build();

// A fronteira sanitizada precede autenticação/autorização; diagnóstico anônimo não dispara consulta de sessão.
app.UseRouting();
// CORS envolve respostas da API e seleciona a exceção de chave por rota/método antes da autenticação.
app.UseCors(FinoraCors.PolicyName);
// A fronteira externa sanitiza exceções não tratadas antes da escrita de respostas de status.
app.UseMiddleware<ApiExceptionBoundary>();
// O limitador do login deve rejeitar antes de AuthenticationBoundary ler ou validar o corpo.
app.UseMiddleware<LoginRequestRateLimitBoundary>();
// Converte respostas de erro sem corpo (incluindo 404 do roteamento) no envelope comum da API.
app.UseStatusCodePages(async statusContext =>
{
    var path = statusContext.HttpContext.Request.Path;
    // Documentação desabilitada mantém o 404 vazio já observado por clientes fora de Development.
    if (path.StartsWithSegments("/swagger") || path.StartsWithSegments("/openapi")) { return; }
    await ApiProblemDetails.WriteAsync(statusContext.HttpContext, statusContext.HttpContext.Response.StatusCode);
});
app.UseMiddleware<AuthenticationBoundary>();

// Swagger UI é middleware sem metadados de endpoint; em Development, servi-lo antes da autorização o mantém anônimo.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Finora v1"));
}

app.UseAuthentication();
app.UseAuthorization();
// Só titulares que passaram pela validação de sessão consomem a partição local de rotas protegidas.
app.UseMiddleware<AuthenticatedRequestRateLimitBoundary>();

// Documento e assets da interface existem exclusivamente no ambiente de desenvolvimento.
if (app.Environment.IsDevelopment())
{
    // O documento é diagnóstico público somente em Development e não herda a política fallback.
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();
// Catch-all anônimo cobre também caminhos com extensão, sem transformar rota inexistente em challenge 401/503.
app.Map("/{**path}", () => Results.NotFound()).AllowAnonymous();
app.Run();
return 0;

// Expõe o entry point ao host de testes sem modificar a inicialização da API.
public partial class Program;
