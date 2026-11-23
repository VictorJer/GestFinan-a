using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fnr013SyncRunExecutionRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutionMessageEnqueuedAtUtc",
                table: "SyncRun",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncRun_State_ExecutionMessageEnqueuedAtUtc",
                table: "SyncRun",
                columns: new[] { "State", "ExecutionMessageEnqueuedAtUtc" },
                filter: "\"ExecutionMessageEnqueuedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncRun_State_ExecutionMessageEnqueuedAtUtc",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "ExecutionMessageEnqueuedAtUtc",
                table: "SyncRun");
        }
    }
}
