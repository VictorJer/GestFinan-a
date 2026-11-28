using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentPortfolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Investment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Class = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    AppliedAmount = table.Column<decimal>(type: "numeric(19,2)", nullable: false),
                    CurrentAmount = table.Column<decimal>(type: "numeric(19,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Investment", x => x.Id);
                    table.CheckConstraint("CK_Investment_AmountsNonNegative", "\"AppliedAmount\" >= 0 AND \"CurrentAmount\" >= 0");
                    table.CheckConstraint("CK_Investment_Class", "\"Class\" IN ('fixed_income', 'variable_income', 'real_estate_funds')");
                    table.CheckConstraint("CK_Investment_IdsNotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Investment_Name", "length(btrim(\"Name\")) > 0 AND length(\"Name\") <= 120 AND \"Name\" = btrim(\"Name\")");
                    table.ForeignKey(
                        name: "FK_Investment_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Investment_UserId_Id",
                table: "Investment",
                columns: new[] { "UserId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Investment");
        }
    }
}
