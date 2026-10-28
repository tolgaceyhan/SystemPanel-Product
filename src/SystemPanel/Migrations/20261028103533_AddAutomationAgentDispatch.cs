using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SystemPanel.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationAgentDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeadlineUtc",
                table: "RunbookRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchedAtUtc",
                table: "RunbookRuns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetAgentId",
                table: "RunbookRuns",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunbookRuns_TargetAgentId_Status",
                table: "RunbookRuns",
                columns: new[] { "TargetAgentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RunbookRuns_TargetAgentId_Status",
                table: "RunbookRuns");

            migrationBuilder.DropColumn(
                name: "DeadlineUtc",
                table: "RunbookRuns");

            migrationBuilder.DropColumn(
                name: "DispatchedAtUtc",
                table: "RunbookRuns");

            migrationBuilder.DropColumn(
                name: "TargetAgentId",
                table: "RunbookRuns");
        }
    }
}
