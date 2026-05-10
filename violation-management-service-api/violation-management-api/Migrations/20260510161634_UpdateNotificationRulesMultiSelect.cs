using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateNotificationRulesMultiSelect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilterCameraId",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "FilterLocationId",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "FilterSeverity",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "FilterViolationTypeId",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "TimeOfDayEnd",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "TimeOfDayStart",
                table: "NotificationRules");

            migrationBuilder.AddColumn<string>(
                name: "FilterCameraIdsJson",
                table: "NotificationRules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilterDepartmentsJson",
                table: "NotificationRules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilterLocationIdsJson",
                table: "NotificationRules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilterSeveritiesJson",
                table: "NotificationRules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FilterViolationTypeIdsJson",
                table: "NotificationRules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TimeIntervalsJson",
                table: "NotificationRules",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilterCameraIdsJson",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "FilterDepartmentsJson",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "FilterLocationIdsJson",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "FilterSeveritiesJson",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "FilterViolationTypeIdsJson",
                table: "NotificationRules");

            migrationBuilder.DropColumn(
                name: "TimeIntervalsJson",
                table: "NotificationRules");

            migrationBuilder.AddColumn<string>(
                name: "FilterCameraId",
                table: "NotificationRules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FilterLocationId",
                table: "NotificationRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FilterSeverity",
                table: "NotificationRules",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FilterViolationTypeId",
                table: "NotificationRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TimeOfDayEnd",
                table: "NotificationRules",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "TimeOfDayStart",
                table: "NotificationRules",
                type: "interval",
                nullable: true);
        }
    }
}
