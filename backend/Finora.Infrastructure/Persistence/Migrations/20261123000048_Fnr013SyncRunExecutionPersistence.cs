using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fnr013SyncRunExecutionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttemptDeadlineAtUtc",
                table: "SyncRun",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptLimit",
                table: "SyncRun",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "AttemptNumber",
                table: "SyncRun",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AttemptStartedAtUtc",
                table: "SyncRun",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKeyHash",
                table: "SyncRun",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                collation: "C");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAtUtc",
                table: "SyncRun",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "SyncRun",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                collation: "C");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SyncRun_Id_ConnectionId_UserId",
                table: "SyncRun",
                columns: new[] { "Id", "ConnectionId", "UserId" });

            migrationBuilder.CreateTable(
                name: "InboxState",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    Received = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceiveCount = table.Column<int>(type: "integer", nullable: false),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Consumed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxState", x => x.Id);
                    table.UniqueConstraint("AK_InboxState_MessageId_ConsumerId", x => new { x.MessageId, x.ConsumerId });
                });

            migrationBuilder.CreateTable(
                name: "OutboxState",
                columns: table => new
                {
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: false),
                    LockId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Delivered = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSequenceNumber = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxState", x => x.OutboxId);
                });

            migrationBuilder.CreateTable(
                name: "SyncRunLease",
                columns: table => new
                {
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    AcquiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FencingToken = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRunLeases", x => x.ConnectionId);
                    table.CheckConstraint("CK_SyncRunLeases_Attempt_Positive", "\"AttemptNumber\" > 0");
                    table.CheckConstraint("CK_SyncRunLeases_Expiration", "\"ExpiresAtUtc\" > \"AcquiredAtUtc\"");
                    table.CheckConstraint("CK_SyncRunLeases_Fencing_Positive", "\"FencingToken\" > 0");
                    table.ForeignKey(
                        name: "FK_SyncRunLease_FinancialConnection_ConnectionId_UserId",
                        columns: x => new { x.ConnectionId, x.UserId },
                        principalTable: "FinancialConnection",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncRunLease_SyncRun_SyncRunId_ConnectionId_UserId",
                        columns: x => new { x.SyncRunId, x.ConnectionId, x.UserId },
                        principalTable: "SyncRun",
                        principalColumns: new[] { "Id", "ConnectionId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                columns: table => new
                {
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EnqueueTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: true),
                    Properties = table.Column<string>(type: "text", nullable: true),
                    InboxMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    InboxConsumerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutboxId = table.Column<Guid>(type: "uuid", nullable: true),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MessageType = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InitiatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DestinationAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ResponseAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FaultAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpirationTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.SequenceNumber);
                    table.ForeignKey(
                        name: "FK_OutboxMessage_InboxState_InboxMessageId_InboxConsumerId",
                        columns: x => new { x.InboxMessageId, x.InboxConsumerId },
                        principalTable: "InboxState",
                        principalColumns: new[] { "MessageId", "ConsumerId" });
                    table.ForeignKey(
                        name: "FK_OutboxMessage_OutboxState_OutboxId",
                        column: x => x.OutboxId,
                        principalTable: "OutboxState",
                        principalColumn: "OutboxId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRun_State_NextAttemptAtUtc",
                table: "SyncRun",
                columns: new[] { "State", "NextAttemptAtUtc" },
                filter: "\"NextAttemptAtUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_SyncRun_IdempotencyKeyHash",
                table: "SyncRun",
                column: "IdempotencyKeyHash",
                unique: true,
                filter: "\"IdempotencyKeyHash\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SyncRun_AttemptBudget",
                table: "SyncRun",
                sql: "\"AttemptNumber\" >= 0 AND \"AttemptLimit\" BETWEEN 1 AND 3 AND \"AttemptNumber\" <= \"AttemptLimit\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SyncRun_AttemptDeadline",
                table: "SyncRun",
                sql: "((\"AttemptStartedAtUtc\" IS NULL AND \"AttemptDeadlineAtUtc\" IS NULL) OR (\"AttemptStartedAtUtc\" IS NOT NULL AND \"AttemptDeadlineAtUtc\" IS NOT NULL AND \"AttemptNumber\" > 0 AND \"AttemptDeadlineAtUtc\" > \"AttemptStartedAtUtc\" AND \"AttemptDeadlineAtUtc\" <= \"AttemptStartedAtUtc\" + interval '10 minutes'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SyncRun_IdempotencyFingerprintPair",
                table: "SyncRun",
                sql: "((\"IdempotencyKeyHash\" IS NULL AND \"RequestFingerprint\" IS NULL) OR (\"IdempotencyKeyHash\" ~ '^[0-9A-Fa-f]{64}$' AND \"RequestFingerprint\" ~ '^[0-9A-Fa-f]{64}$'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SyncRun_NextAttemptNotBeforeAcceptance",
                table: "SyncRun",
                sql: "\"NextAttemptAtUtc\" IS NULL OR \"NextAttemptAtUtc\" >= \"CreatedAtUtc\"");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_EnqueueTime",
                table: "OutboxMessage",
                column: "EnqueueTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_ExpirationTime",
                table: "OutboxMessage",
                column: "ExpirationTime");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "InboxMessageId", "InboxConsumerId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_OutboxId_SequenceNumber",
                table: "OutboxMessage",
                columns: new[] { "OutboxId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxState_Created",
                table: "OutboxState",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunLease_ConnectionId_UserId",
                table: "SyncRunLease",
                columns: new[] { "ConnectionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunLease_SyncRunId_ConnectionId_UserId",
                table: "SyncRunLease",
                columns: new[] { "SyncRunId", "ConnectionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRunLeases_SyncRunId_AttemptNumber",
                table: "SyncRunLease",
                columns: new[] { "SyncRunId", "AttemptNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessage");

            migrationBuilder.DropTable(
                name: "SyncRunLease");

            migrationBuilder.DropTable(
                name: "InboxState");

            migrationBuilder.DropTable(
                name: "OutboxState");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SyncRun_Id_ConnectionId_UserId",
                table: "SyncRun");

            migrationBuilder.DropIndex(
                name: "IX_SyncRun_State_NextAttemptAtUtc",
                table: "SyncRun");

            migrationBuilder.DropIndex(
                name: "UX_SyncRun_IdempotencyKeyHash",
                table: "SyncRun");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SyncRun_AttemptBudget",
                table: "SyncRun");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SyncRun_AttemptDeadline",
                table: "SyncRun");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SyncRun_IdempotencyFingerprintPair",
                table: "SyncRun");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SyncRun_NextAttemptNotBeforeAcceptance",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "AttemptDeadlineAtUtc",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "AttemptLimit",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "AttemptNumber",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "AttemptStartedAtUtc",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "IdempotencyKeyHash",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "SyncRun");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "SyncRun");
        }
    }
}
