using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeToViolations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "Violations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Violations_EmployeeId",
                table: "Violations",
                column: "EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Violations_Employees_EmployeeId",
                table: "Violations",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Violations_Employees_EmployeeId",
                table: "Violations");

            migrationBuilder.DropIndex(
                name: "IX_Violations_EmployeeId",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "Violations");
        }
    }
}
