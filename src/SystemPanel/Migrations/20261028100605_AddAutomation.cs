using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemPanel.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunbookSigningKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PublicKeyPem = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    IsRevoked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunbookSigningKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunbookVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunbookId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Risk = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Executor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SupportsDryRun = table.Column<bool>(type: "bit", nullable: false),
                    ScriptSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KeyFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PackageText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunbookVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunbookRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunbookVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetSummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Justification = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequiredApprovals = table.Column<int>(type: "int", nullable: false),
                    ApprovalExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviewOutput = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExitCode = table.Column<int>(type: "int", nullable: true),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunbookRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunbookRuns_RunbookVersions_RunbookVersionId",
                        column: x => x.RunbookVersionId,
                        principalTable: "RunbookVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RunbookApprovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Approver = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunbookApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunbookApprovals_RunbookRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "RunbookRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunbookApprovals_RunId",
                table: "RunbookApprovals",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_RunbookRuns_RequestedAtUtc",
                table: "RunbookRuns",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RunbookRuns_RunbookVersionId",
                table: "RunbookRuns",
                column: "RunbookVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunbookRuns_Status",
                table: "RunbookRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_RunbookSigningKeys_Fingerprint",
                table: "RunbookSigningKeys",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunbookVersions_RunbookId_Version",
                table: "RunbookVersions",
                columns: new[] { "RunbookId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunbookApprovals");

            migrationBuilder.DropTable(
                name: "RunbookSigningKeys");

            migrationBuilder.DropTable(
                name: "RunbookRuns");

            migrationBuilder.DropTable(
                name: "RunbookVersions");
        }
    }
}
