using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fnr038RecurrenceAssistance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurrenceCandidate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    FirstOccurrenceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SecondOccurrenceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThirdOccurrenceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastEvaluatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurrenceCandidate", x => x.Id);
                    table.UniqueConstraint("AK_RecurrenceCandidate_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_RecurrenceCandidate_EvaluationRange", "\"LastEvaluatedAtUtc\" >= \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_RecurrenceCandidate_IdsNotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"FirstOccurrenceTransactionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"SecondOccurrenceTransactionId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ThirdOccurrenceTransactionId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RecurrenceCandidate_OccurrencesDistinct", "\"FirstOccurrenceTransactionId\" <> \"SecondOccurrenceTransactionId\" AND \"FirstOccurrenceTransactionId\" <> \"ThirdOccurrenceTransactionId\" AND \"SecondOccurrenceTransactionId\" <> \"ThirdOccurrenceTransactionId\"");
                    table.CheckConstraint("CK_RecurrenceCandidate_PolicyVersionRequired", "length(btrim(\"PolicyVersion\")) > 0");
                    table.ForeignKey(
                        name: "FK_RecurrenceCandidate_FinancialTransaction_FirstOccurrenceTra~",
                        columns: x => new { x.FirstOccurrenceTransactionId, x.UserId },
                        principalTable: "FinancialTransaction",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurrenceCandidate_FinancialTransaction_SecondOccurrenceTr~",
                        columns: x => new { x.SecondOccurrenceTransactionId, x.UserId },
                        principalTable: "FinancialTransaction",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecurrenceCandidate_FinancialTransaction_ThirdOccurrenceTra~",
                        columns: x => new { x.ThirdOccurrenceTransactionId, x.UserId },
                        principalTable: "FinancialTransaction",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurrenceDecision",
                columns: table => new
                {
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurrenceDecision", x => new { x.CandidateId, x.UserId });
                    table.CheckConstraint("CK_RecurrenceDecision_IdsNotEmpty", "\"CandidateId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_RecurrenceDecision_Kind", "\"Kind\" IN ('Subscription', 'Installment', 'IndependentPurchases', 'Rejected')");
                    table.ForeignKey(
                        name: "FK_RecurrenceDecision_RecurrenceCandidate_CandidateId_UserId",
                        columns: x => new { x.CandidateId, x.UserId },
                        principalTable: "RecurrenceCandidate",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceCandidate_CurrentOwnerPolicyCreated",
                table: "RecurrenceCandidate",
                columns: new[] { "UserId", "PolicyVersion", "CreatedAtUtc" },
                filter: "\"IsCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceCandidate_FirstOccurrenceOwner",
                table: "RecurrenceCandidate",
                columns: new[] { "FirstOccurrenceTransactionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceCandidate_SecondOccurrenceOwner",
                table: "RecurrenceCandidate",
                columns: new[] { "SecondOccurrenceTransactionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceCandidate_ThirdOccurrenceOwner",
                table: "RecurrenceCandidate",
                columns: new[] { "ThirdOccurrenceTransactionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "UX_RecurrenceCandidate_OwnerPolicyOccurrences",
                table: "RecurrenceCandidate",
                columns: new[] { "UserId", "PolicyVersion", "FirstOccurrenceTransactionId", "SecondOccurrenceTransactionId", "ThirdOccurrenceTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceDecision_OwnerCandidate",
                table: "RecurrenceDecision",
                columns: new[] { "UserId", "CandidateId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback só remove o esquema vazio; decisões ou candidatos existentes exigem plano de preservação explícito.
            migrationBuilder.Sql("""
                DO $recurrence_rollback$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "RecurrenceDecision")
                       OR EXISTS (SELECT 1 FROM "RecurrenceCandidate") THEN
                        RAISE EXCEPTION 'Rollback de recorrência recusado para preservar candidatos e decisões do titular';
                    END IF;
                END
                $recurrence_rollback$;
                """);

            migrationBuilder.DropTable(
                name: "RecurrenceDecision");

            migrationBuilder.DropTable(
                name: "RecurrenceCandidate");
        }
    }
}
