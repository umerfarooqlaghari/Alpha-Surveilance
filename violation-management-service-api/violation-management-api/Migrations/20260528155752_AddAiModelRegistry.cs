using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiModelId",
                table: "SopViolationTypes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ModelType = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    DownloadUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    S3Bucket = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    S3Key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LocalPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModels", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SopViolationTypes_AiModelId",
                table: "SopViolationTypes",
                column: "AiModelId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_ModelKey",
                table: "AiModels",
                column: "ModelKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SopViolationTypes_AiModels_AiModelId",
                table: "SopViolationTypes",
                column: "AiModelId",
                principalTable: "AiModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Seed the four built-in models ──────────────────────────────────
            migrationBuilder.Sql(@"
                INSERT INTO ""AiModels"" (
                    ""Id"", ""ModelKey"", ""DisplayName"", ""Description"",
                    ""ModelType"", ""Status"",
                    ""S3Bucket"", ""S3Key"", ""LocalPath"",
                    ""Version"", ""CreatedAt"", ""IsDeleted""
                ) VALUES
                (
                    'a0000000-0000-0000-0000-000000000001',
                    'human-detection-v1', 'Human Detection YOLO 11n',
                    'Lightweight YOLO 11n model for detecting persons in restricted areas.',
                    'YoloLocal', 'Available',
                    NULL, NULL, '/tmp/models/yolo11n.pt',
                    '1.0', NOW() AT TIME ZONE 'UTC', false
                ),
                (
                    'a0000000-0000-0000-0000-000000000002',
                    'restaurant-ppe-v1', 'Restaurant PPE YOLO 11m v2',
                    'Fine-tuned YOLO 11m for restaurant PPE compliance (hairnet, mask, gloves).',
                    'YoloFineTuned', 'Available',
                    'restaurant-ppe-yolo11-pt4-v1--use1-az4--x-s3',
                    'models/restaurant-ppe-yolo11m-v2.pt',
                    '/tmp/models/restaurant-ppe-yolo11m-v2.pt',
                    '2.0', NOW() AT TIME ZONE 'UTC', false
                ),
                (
                    'a0000000-0000-0000-0000-000000000003',
                    'pest-detection-v1', 'Kitchen Pest Detector YOLO 11m',
                    'Fine-tuned YOLO 11m for cockroach, lizard and rat detection in food areas.',
                    'YoloFineTuned', 'Registered',
                    'alphasurveilance-dev-1',
                    'models/kitchen-pest-yolo11m.pt',
                    '/tmp/models/kitchen-pest-yolo11m.pt',
                    '1.0', NOW() AT TIME ZONE 'UTC', false
                ),
                (
                    'a0000000-0000-0000-0000-000000000004',
                    'hustvl/yolos-tiny', 'YOLOS-Tiny (Roboflow Cloud)',
                    'Roboflow-hosted YOLOS-tiny model used for general person detection via cloud API.',
                    'RoboflowCloud', 'Available',
                    NULL, NULL, NULL,
                    '1.0', NOW() AT TIME ZONE 'UTC', false
                )
                ON CONFLICT (""ModelKey"") DO NOTHING;
            ");

            // ── Back-fill FK on existing SopViolationTypes ─────────────────────
            migrationBuilder.Sql(@"
                UPDATE ""SopViolationTypes"" svt
                SET ""AiModelId"" = m.""Id""
                FROM ""AiModels"" m
                WHERE svt.""ModelIdentifier"" = m.""ModelKey""
                  AND svt.""AiModelId"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SopViolationTypes_AiModels_AiModelId",
                table: "SopViolationTypes");

            migrationBuilder.DropTable(
                name: "AiModels");

            migrationBuilder.DropIndex(
                name: "IX_SopViolationTypes_AiModelId",
                table: "SopViolationTypes");

            migrationBuilder.DropColumn(
                name: "AiModelId",
                table: "SopViolationTypes");
        }
    }
}
