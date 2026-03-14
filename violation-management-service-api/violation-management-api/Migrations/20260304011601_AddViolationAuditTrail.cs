using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddViolationAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ViolationAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ViolationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "text", nullable: true),
                    RootCauseAnalysis = table.Column<string>(type: "text", nullable: true),
                    ContributingFactors = table.Column<string>(type: "text", nullable: true),
                    StakeholdersAffected = table.Column<string>(type: "text", nullable: true),
                    EstimatedImpact = table.Column<string>(type: "text", nullable: true),
                    MeasuresTaken = table.Column<string>(type: "text", nullable: true),
                    ResolvedBy = table.Column<string>(type: "text", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreventionMeasures = table.Column<string>(type: "text", nullable: true),
                    FollowUpActions = table.Column<string>(type: "text", nullable: true),
                    ReviewedBy = table.Column<string>(type: "text", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InternalNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViolationAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ViolationAudits_Violations_ViolationId",
                        column: x => x.ViolationId,
                        principalTable: "Violations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ViolationAudits_TenantId",
                table: "ViolationAudits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ViolationAudits_ViolationId",
                table: "ViolationAudits",
                column: "ViolationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ViolationAudits");
        }
    }
}
