using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.CheckConstraint("CK_AuthSessions_Expiration", "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_AuthSessions_Revocation", "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"CreatedAtUtc\"");
                    table.ForeignKey(
                        name: "FK_AuthSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_UserId",
                table: "AuthSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthSessions");
        }
    }
}
