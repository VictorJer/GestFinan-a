using Finora.Api;
using Finora.Application.Pluggy;
using Finora.Infrastructure.Pluggy;
using MediatR;

// Qualquer argumento de processo é tratado como comando, exceto a assinatura fixa do host de testes.
if (args.Length > 0 && !PluggyReadCommand.IsApiTestHostInvocation(args))
{
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
// O gerador nativo descreve os Controllers; a UI não adiciona outro gerador.
builder.Services.AddOpenApi();

// Publica somente os Controllers implementados; diagnóstico não implica prontidão de integrações.
var app = builder.Build();

// Documento e assets da interface existem exclusivamente no ambiente de desenvolvimento.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Finora v1"));
}

app.MapControllers();
app.Run();
return 0;

// Expõe o entry point ao host de testes sem modificar a inicialização da API.
public partial class Program;
