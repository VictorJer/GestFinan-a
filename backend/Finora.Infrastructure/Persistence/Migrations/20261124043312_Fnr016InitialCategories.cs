using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fnr016InitialCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Trava a tabela dentro da transação do EF para validar todas as reservas antes de inserir cada ID estável.
            migrationBuilder.Sql("""
                LOCK TABLE "Category" IN SHARE ROW EXCLUSIVE MODE;
                DO $catalog$
                DECLARE
                    entry record;
                BEGIN
                    -- A opção de ausência não pode ocupar uma linha de sistema ou um nome pessoal reservado.
                    IF EXISTS (
                        SELECT 1 FROM "Category"
                        WHERE "Name" = 'Sem categoria' OR "NormalizedName" = 'SEM CATEGORIA'
                    ) THEN
                        RAISE EXCEPTION 'Opção virtual Sem categoria persistida; intervenção administrativa necessária';
                    END IF;

                    FOR entry IN
                        SELECT * FROM (VALUES
                            ('4e2fc103-67f4-4f5f-82dc-62c3ddf55c01'::uuid, 'Alimentação', 'ALIMENTACAO'),
                            ('4e2fc103-67f4-4f5f-82dc-62c3ddf55c02'::uuid, 'Transporte', 'TRANSPORTE'),
                            ('4e2fc103-67f4-4f5f-82dc-62c3ddf55c03'::uuid, 'Compras', 'COMPRAS'),
                            ('4e2fc103-67f4-4f5f-82dc-62c3ddf55c04'::uuid, 'Lazer', 'LAZER'),
                            ('4e2fc103-67f4-4f5f-82dc-62c3ddf55c05'::uuid, 'Saúde', 'SAUDE'),
                            ('4e2fc103-67f4-4f5f-82dc-62c3ddf55c06'::uuid, 'Educação', 'EDUCACAO'),
                            ('4e2fc103-67f4-4f5f-82dc-62c3ddf55c07'::uuid, 'Outros', 'OUTROS')
                        ) AS expected(id, name, key)
                    LOOP
                        -- Qualquer uso incompatível de ID, nome ou chave reservados aborta sem corrigir dados legados.
                        IF EXISTS (
                            SELECT 1 FROM "Category" AS c
                            WHERE (
                                c."Id" = entry.id
                                OR (c."IsSystem" AND (c."Name" = entry.name OR c."NormalizedName" = entry.key))
                                OR (NOT c."IsSystem" AND c."NormalizedName" = entry.key)
                            )
                            AND NOT (
                                c."Id" = entry.id AND c."UserId" IS NULL AND c."IsSystem"
                                AND c."ScopeId" = '00000000-0000-0000-0000-000000000000'::uuid
                                AND c."Name" = entry.name AND c."NormalizedName" = entry.key
                            )
                        ) THEN
                            RAISE EXCEPTION 'Catálogo de categorias incompatível; intervenção administrativa necessária';
                        END IF;

                        -- Uma reaplicação sem histórico não duplica linhas já verificadas; a transação reverte tudo em falha.
                        INSERT INTO "Category" ("Id", "UserId", "IsSystem", "Name", "NormalizedName", "ScopeId")
                        SELECT entry.id, NULL, TRUE, entry.name, entry.key,
                               '00000000-0000-0000-0000-000000000000'::uuid
                        WHERE NOT EXISTS (SELECT 1 FROM "Category" AS c WHERE c."Id" = entry.id);
                    END LOOP;
                END
                $catalog$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverter o código não remove categorias possivelmente referenciadas por anotações pessoais.
        }
    }
}
