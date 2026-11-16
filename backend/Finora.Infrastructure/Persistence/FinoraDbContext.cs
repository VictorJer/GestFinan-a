using Microsoft.EntityFrameworkCore;

namespace Finora.Infrastructure.Persistence;

/// <summary>Contexto técnico inicial para manter o provider e as migrations na Infrastructure.</summary>
// Esta versão não declara entidades; o modelo relacional de negócio pertence à FNR-007.
public sealed class FinoraDbContext(DbContextOptions<FinoraDbContext> options) : DbContext(options)
{
}
