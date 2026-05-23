using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddFalsePositiveFlagToViolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FalsePositiveMarkedAt",
                table: "Violations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FalsePositiveMarkedBy",
                table: "Violations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FalsePositiveReason",
                table: "Violations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFalsePositive",
                table: "Violations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Violations_TenantId_IsFalsePositive_Timestamp",
                table: "Violations",
                columns: new[] { "TenantId", "IsFalsePositive", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Violations_TenantId_IsFalsePositive_Timestamp",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "FalsePositiveMarkedAt",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "FalsePositiveMarkedBy",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "FalsePositiveReason",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "IsFalsePositive",
                table: "Violations");
        }
    }
}
