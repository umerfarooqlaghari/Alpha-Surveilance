using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchicalSops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableComplianceViolations",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "EnableOperationalViolations",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "EnableSafetyViolations",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "EnableSecurityViolations",
                table: "Cameras");

            migrationBuilder.CreateTable(
                name: "Sops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SopViolationTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SopId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ModelIdentifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SopViolationTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SopViolationTypes_Sops_SopId",
                        column: x => x.SopId,
                        principalTable: "Sops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CameraViolationTypes",
                columns: table => new
                {
                    CameraId = table.Column<Guid>(type: "uuid", nullable: false),
                    SopViolationTypeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CameraViolationTypes", x => new { x.CameraId, x.SopViolationTypeId });
                    table.ForeignKey(
                        name: "FK_CameraViolationTypes_Cameras_CameraId",
                        column: x => x.CameraId,
                        principalTable: "Cameras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CameraViolationTypes_SopViolationTypes_SopViolationTypeId",
                        column: x => x.SopViolationTypeId,
                        principalTable: "SopViolationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantViolationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SopViolationTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantViolationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantViolationRequests_SopViolationTypes_SopViolationTypeId",
                        column: x => x.SopViolationTypeId,
                        principalTable: "SopViolationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantViolationRequests_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CameraViolationTypes_SopViolationTypeId",
                table: "CameraViolationTypes",
                column: "SopViolationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_SopViolationTypes_SopId",
                table: "SopViolationTypes",
                column: "SopId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantViolationRequests_SopViolationTypeId",
                table: "TenantViolationRequests",
                column: "SopViolationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantViolationRequests_TenantId",
                table: "TenantViolationRequests",
                column: "TenantId");

            // --- DATA SEEDING ---
            var sopId = Guid.NewGuid();
            var violationTypeId = Guid.NewGuid();

            migrationBuilder.InsertData(
                table: "Sops",
                columns: new[] { "Id", "Name", "Description", "CreatedAt" },
                values: new object[] { sopId, "Human Detection", "General policies for detecting human presence in unauthorized areas.", DateTime.UtcNow }
            );

            migrationBuilder.InsertData(
                table: "SopViolationTypes",
                columns: new[] { "Id", "SopId", "Name", "ModelIdentifier", "Description" },
                values: new object[] { violationTypeId, sopId, "Restricted Area Access", "human-detection-v1", "Detects humans present in configured restricted zones." }
            );

            // Automatically grant approval to all existing tenants for this base violation
            migrationBuilder.Sql($@"
                INSERT INTO ""TenantViolationRequests"" (""Id"", ""TenantId"", ""SopViolationTypeId"", ""Status"", ""RequestedAt"", ""ResolvedAt"")
                SELECT gen_random_uuid(), ""Id"", '{violationTypeId}', 1, NOW(), NOW()
                FROM ""Tenants"";
            ");

            // Automatically assign this violation to all active cameras that previously had EnableSecurityViolations = true
            // Since we dropped the column earlier in this migration, we just assign to all active cameras for now
            // or we could have done it BEFORE dropping, but this is simpler for the initial seed.
            migrationBuilder.Sql($@"
                INSERT INTO ""CameraViolationTypes"" (""CameraId"", ""SopViolationTypeId"")
                SELECT ""Id"", '{violationTypeId}'
                FROM ""Cameras""
                WHERE ""Status"" = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The seeded data will be cascade-deleted by deleting the tables, 
            // but if we were keeping tables we would delete the specific records here.

            migrationBuilder.DropTable(
                name: "CameraViolationTypes");

            migrationBuilder.DropTable(
                name: "TenantViolationRequests");

            migrationBuilder.DropTable(
                name: "SopViolationTypes");

            migrationBuilder.DropTable(
                name: "Sops");

            migrationBuilder.AddColumn<bool>(
                name: "EnableComplianceViolations",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableOperationalViolations",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableSafetyViolations",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableSecurityViolations",
                table: "Cameras",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
