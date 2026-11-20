using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialConnectionItemIdentityHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinancialConnectionItemIdentity",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    ExternalItemId = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialConnectionItemIdentity", x => new { x.Provider, x.ExternalItemId });
                    table.CheckConstraint("CK_FinancialConnectionItemIdentity_ExternalItemId_MaxUtf8Bytes", "octet_length(convert_to(\"ExternalItemId\", 'UTF8')) <= 1024");
                    table.ForeignKey(
                        name: "FK_FinancialConnectionItemIdentity_FinancialConnection_Connect~",
                        columns: x => new { x.ConnectionId, x.UserId },
                        principalTable: "FinancialConnection",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Backfill conserva o item atualmente associado; a PK interrompe o upgrade diante de colisão global.
            migrationBuilder.Sql(
                "INSERT INTO \"FinancialConnectionItemIdentity\" (\"Provider\", \"ExternalItemId\", \"ConnectionId\", \"UserId\", \"RetiredAtUtc\") " +
                "SELECT \"Provider\", \"ExternalItemId\", \"Id\", \"UserId\", NULL FROM \"FinancialConnection\";");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialConnectionItemIdentity_ConnectionId",
                table: "FinancialConnectionItemIdentity",
                column: "ConnectionId",
                unique: true,
                filter: "\"RetiredAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialConnectionItemIdentity_ConnectionId_UserId",
                table: "FinancialConnectionItemIdentity",
                columns: new[] { "ConnectionId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialConnectionItemIdentity");
        }
    }
}
