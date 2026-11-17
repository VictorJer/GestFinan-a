using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Finora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fnr007CorePersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.CheckConstraint("CK_AspNetUsers_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false, collation: "C"),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.UniqueConstraint("AK_Category_Id_ScopeId", x => new { x.Id, x.ScopeId });
                    table.CheckConstraint("CK_Category_Id_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_Category_SystemOwnershipAndScope", "((\"IsSystem\" = TRUE AND \"UserId\" IS NULL AND \"ScopeId\" = '00000000-0000-0000-0000-000000000000'::uuid) OR (\"IsSystem\" = FALSE AND \"UserId\" IS NOT NULL AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"ScopeId\" = \"UserId\"))");
                    table.ForeignKey(
                        name: "FK_Category_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialConnection",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, collation: "C"),
                    ExternalItemId = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    SourceStatus = table.Column<string>(type: "text", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessfulImportAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceUpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialConnection", x => x.Id);
                    table.UniqueConstraint("AK_FinancialConnection_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_FinancialConnection_ExternalItemId_MaxUtf8Bytes", "octet_length(convert_to(\"ExternalItemId\", 'UTF8')) <= 1024");
                    table.CheckConstraint("CK_FinancialConnection_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_FinancialConnection_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardAccount",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "text", nullable: true),
                    LastFour = table.Column<string>(type: "text", nullable: true),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    AvailableCreditLimit = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    TotalCreditLimit = table.Column<decimal>(type: "numeric(19,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardAccount", x => x.Id);
                    table.UniqueConstraint("AK_CardAccount_Id_UserId_ExternalId", x => new { x.Id, x.UserId, x.ExternalId });
                    table.CheckConstraint("CK_CardAccount_ExternalId_MaxUtf8Bytes", "octet_length(convert_to(\"ExternalId\", 'UTF8')) <= 1024");
                    table.CheckConstraint("CK_CardAccount_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_CardAccount_FinancialConnection_ConnectionId_UserId",
                        columns: x => new { x.ConnectionId, x.UserId },
                        principalTable: "FinancialConnection",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncRun",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedToExclusive = table.Column<DateOnly>(type: "date", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    PagesRead = table.Column<int>(type: "integer", nullable: true),
                    ReceivedCount = table.Column<int>(type: "integer", nullable: true),
                    UniqueCount = table.Column<int>(type: "integer", nullable: true),
                    PersistedCount = table.Column<int>(type: "integer", nullable: true),
                    ObservedFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ObservedTo = table.Column<DateOnly>(type: "date", nullable: true),
                    CoverageState = table.Column<string>(type: "text", nullable: false),
                    FailureCode = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncRun", x => x.Id);
                    table.CheckConstraint("CK_SyncRun_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_SyncRun_FinancialConnection_ConnectionId_UserId",
                        columns: x => new { x.ConnectionId, x.UserId },
                        principalTable: "FinancialConnection",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialTransaction",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    ExternalAccountId = table.Column<string>(type: "text", nullable: false, collation: "C"),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false),
                    OriginalDate = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    OperationType = table.Column<string>(type: "text", nullable: true),
                    InstallmentNumber = table.Column<int>(type: "integer", nullable: true),
                    TotalInstallments = table.Column<int>(type: "integer", nullable: true),
                    PurchaseDate = table.Column<string>(type: "text", nullable: true),
                    BillId = table.Column<string>(type: "text", nullable: true),
                    BillPostDate = table.Column<string>(type: "text", nullable: true),
                    BillForecastDate = table.Column<string>(type: "text", nullable: true),
                    PaymentType = table.Column<string>(type: "text", nullable: true),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Nature = table.Column<string>(type: "text", nullable: false),
                    NatureReason = table.Column<string>(type: "text", nullable: true),
                    Situation = table.Column<string>(type: "text", nullable: false),
                    SituationReason = table.Column<string>(type: "text", nullable: true),
                    DateReason = table.Column<string>(type: "text", nullable: true),
                    NormalizationRuleVersion = table.Column<string>(type: "text", nullable: false),
                    NormalizationReferenceDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialTransaction", x => x.Id);
                    table.UniqueConstraint("AK_FinancialTransaction_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_FinancialTransaction_ExternalAccountId_MaxUtf8Bytes", "octet_length(convert_to(\"ExternalAccountId\", 'UTF8')) <= 1024");
                    table.CheckConstraint("CK_FinancialTransaction_ExternalId_MaxUtf8Bytes", "octet_length(convert_to(\"ExternalId\", 'UTF8')) <= 1024");
                    table.CheckConstraint("CK_FinancialTransaction_Ids_NotEmpty", "\"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"UserId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.ForeignKey(
                        name: "FK_FinancialTransaction_CardAccount_CardAccountId_UserId_Exter~",
                        columns: x => new { x.CardAccountId, x.UserId, x.ExternalAccountId },
                        principalTable: "CardAccount",
                        principalColumns: new[] { "Id", "UserId", "ExternalId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionAnnotation",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryScopeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionAnnotation", x => x.TransactionId);
                    table.CheckConstraint("CK_TransactionAnnotation_CategoryAndScope", "((\"CategoryId\" IS NULL AND \"CategoryScopeId\" IS NULL) OR (\"CategoryId\" IS NOT NULL AND \"CategoryScopeId\" IS NOT NULL AND (\"CategoryScopeId\" = \"UserId\" OR \"CategoryScopeId\" = '00000000-0000-0000-0000-000000000000'::uuid)))");
                    table.ForeignKey(
                        name: "FK_TransactionAnnotation_Category_CategoryId_CategoryScopeId",
                        columns: x => new { x.CategoryId, x.CategoryScopeId },
                        principalTable: "Category",
                        principalColumns: new[] { "Id", "ScopeId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionAnnotation_FinancialTransaction_TransactionId_Us~",
                        columns: x => new { x.TransactionId, x.UserId },
                        principalTable: "FinancialTransaction",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardAccount_ConnectionId_ExternalId",
                table: "CardAccount",
                columns: new[] { "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CardAccount_ConnectionId_UserId",
                table: "CardAccount",
                columns: new[] { "ConnectionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Category_ScopeId_NormalizedName",
                table: "Category",
                columns: new[] { "ScopeId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Category_UserId",
                table: "Category",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialConnection_UserId_Provider_ExternalItemId",
                table: "FinancialConnection",
                columns: new[] { "UserId", "Provider", "ExternalItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_CardAccountId_ExternalId",
                table: "FinancialTransaction",
                columns: new[] { "CardAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_CardAccountId_TransactionDate_Id",
                table: "FinancialTransaction",
                columns: new[] { "CardAccountId", "TransactionDate", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialTransaction_CardAccountId_UserId_ExternalAccountId",
                table: "FinancialTransaction",
                columns: new[] { "CardAccountId", "UserId", "ExternalAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRun_ConnectionId_State",
                table: "SyncRun",
                columns: new[] { "ConnectionId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncRun_ConnectionId_UserId",
                table: "SyncRun",
                columns: new[] { "ConnectionId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAnnotation_CategoryId_CategoryScopeId",
                table: "TransactionAnnotation",
                columns: new[] { "CategoryId", "CategoryScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionAnnotation_TransactionId_UserId",
                table: "TransactionAnnotation",
                columns: new[] { "TransactionId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "SyncRun");

            migrationBuilder.DropTable(
                name: "TransactionAnnotation");

            migrationBuilder.DropTable(
                name: "Category");

            migrationBuilder.DropTable(
                name: "FinancialTransaction");

            migrationBuilder.DropTable(
                name: "CardAccount");

            migrationBuilder.DropTable(
                name: "FinancialConnection");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
