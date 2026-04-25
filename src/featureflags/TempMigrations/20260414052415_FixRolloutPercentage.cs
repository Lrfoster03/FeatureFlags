using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.TempMigrations
{
    /// <inheritdoc />
    public partial class FixRolloutPercentage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Rollout",
                table: "FeatureFlags",
                newName: "PercentageRollout");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PercentageRollout",
                table: "FeatureFlags",
                newName: "Rollout");
        }
    }
}
