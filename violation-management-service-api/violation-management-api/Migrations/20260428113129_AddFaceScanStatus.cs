using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceScanStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FaceScanCompletedAt",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FaceScanInviteSentAt",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaceScanStatus",
                table: "Employees",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaceScanCompletedAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "FaceScanInviteSentAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "FaceScanStatus",
                table: "Employees");
        }
    }
}
