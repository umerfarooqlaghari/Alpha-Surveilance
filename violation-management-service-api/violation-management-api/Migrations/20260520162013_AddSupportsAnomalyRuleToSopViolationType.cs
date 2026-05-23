using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace violation_management_api.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportsAnomalyRuleToSopViolationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SupportsAnomalyRule",
                table: "SopViolationTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // D-9 backfill: any existing rule whose TriggerLabels contains a
            // PPE-prefix label ("no-...", "incorrect-...", "missing-...") was
            // previously detected as anomaly-capable by the client-side regex
            // ``PPE_LABEL_RE`` in CameraFormModal.tsx.  Preserve that behavior
            // for already-deployed rows so the upgrade is non-disruptive.
            migrationBuilder.Sql(@"
                UPDATE ""SopViolationTypes""
                SET ""SupportsAnomalyRule"" = TRUE
                WHERE ""TriggerLabels"" ~* '""(no[-_]|incorrect[-_]|missing[-_])';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SupportsAnomalyRule",
                table: "SopViolationTypes");
        }
    }
}
