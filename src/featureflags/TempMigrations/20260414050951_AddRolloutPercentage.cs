using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.TempMigrations
{
    /// <inheritdoc />
    public partial class AddRolloutPercentage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PercentageRollout",
                table: "FeatureFlags",
                newName: "Rollout");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Rollout",
                table: "FeatureFlags",
                newName: "PercentageRollout");
        }
    }
}
