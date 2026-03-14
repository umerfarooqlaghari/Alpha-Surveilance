using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class FixViolationTenantIdType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Violations_Tenants_TenantId1",
                table: "Violations");

            migrationBuilder.DropIndex(
                name: "IX_Violations_TenantId1",
                table: "Violations");

            migrationBuilder.DropColumn(
                name: "TenantId1",
                table: "Violations");

            // migrationBuilder.AlterColumn<Guid>(
            //     name: "TenantId",
            //     table: "Violations",
            //     type: "uuid",
            //     nullable: false,
            //     oldClrType: typeof(string),
            //     oldType: "text");
            
            // migrationBuilder.AlterColumn<Guid>(
            //     name: "TenantId",
            //     table: "Violations",
            //     type: "uuid",
            //     nullable: false,
            //     oldClrType: typeof(string),
            //     oldType: "text");
            
            // Delete violations with invalid TenantId before conversion to prevent casting errors
            // Use Regex to ensure only valid UUIDs remain.
            // Pattern: 8-4-4-4-12 hex digits
            migrationBuilder.Sql("DELETE FROM \"Violations\" WHERE \"TenantId\" !~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$';");
            
            migrationBuilder.Sql("ALTER TABLE \"Violations\" ALTER COLUMN \"TenantId\" TYPE uuid USING \"TenantId\"::uuid;");

            migrationBuilder.AddForeignKey(
                name: "FK_Violations_Tenants_TenantId",
                table: "Violations",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Violations_Tenants_TenantId",
                table: "Violations");

            migrationBuilder.AlterColumn<string>(
                name: "TenantId",
                table: "Violations",
                type: "text",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId1",
                table: "Violations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Violations_TenantId1",
                table: "Violations",
                column: "TenantId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Violations_Tenants_TenantId1",
                table: "Violations",
                column: "TenantId1",
                principalTable: "Tenants",
                principalColumn: "Id");
        }
    }
}
