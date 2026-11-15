using System.Runtime.CompilerServices;

// Restringe a inspeção do cliente interno aos testes da solução, sem ampliar a API de produção.
[assembly: InternalsVisibleTo("Finora.Api.Tests")]
