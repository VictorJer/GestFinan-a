using Microsoft.AspNetCore.Identity;

namespace Finora.Infrastructure.Identity;

/// <summary>Usuário mínimo do Identity com identificador Guid sempre não vazio.</summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Cria usuário compatível com a factory padrão do Identity usando um UUID gerado na aplicação.</summary>
    public ApplicationUser()
        : this(Guid.NewGuid())
    {
    }

    /// <summary>Cria usuário com UUID fornecido, rejeitando o marcador vazio reservado pelo modelo.</summary>
    public ApplicationUser(Guid id)
    {
        Id = id;
    }

    // A validação no setter também protege alterações posteriores e materialização pelo EF Core.
    public override Guid Id
    {
        get => base.Id;
        set
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("O UUID do usuário não pode ser vazio.", nameof(value));
            }

            base.Id = value;
        }
    }
}
