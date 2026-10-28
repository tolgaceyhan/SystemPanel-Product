using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemPanel.Migrations
{
    /// <inheritdoc />
    public partial class AddRunbookSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleId",
                table: "RunbookRuns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RunbookSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RunbookVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TargetSummary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    TriggerType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CronExpression = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AlarmMetric = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    AlarmDrive = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    MinIntervalMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DisabledBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DisabledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextFireAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFiredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunbookSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunbookSchedules_RunbookVersions_RunbookVersionId",
                        column: x => x.RunbookVersionId,
                        principalTable: "RunbookVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunbookRuns_ScheduleId",
                table: "RunbookRuns",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_RunbookSchedules_RunbookVersionId",
                table: "RunbookSchedules",
                column: "RunbookVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RunbookSchedules_Status_NextFireAtUtc",
                table: "RunbookSchedules",
                columns: new[] { "Status", "NextFireAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunbookSchedules_TargetAgentId_TriggerType_Status",
                table: "RunbookSchedules",
                columns: new[] { "TargetAgentId", "TriggerType", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunbookSchedules");

            migrationBuilder.DropIndex(
                name: "IX_RunbookRuns_ScheduleId",
                table: "RunbookRuns");

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "RunbookRuns");
        }
    }
}
