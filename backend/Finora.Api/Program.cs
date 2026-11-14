// Registra Controllers sem persistência, autenticação fictícia ou integrações financeiras.
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

// Expõe o entry point ao host de testes sem modificar a inicialização da API.
public partial class Program;
