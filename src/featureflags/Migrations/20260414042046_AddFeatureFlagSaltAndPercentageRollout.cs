using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FeatureFlags.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureFlagSaltAndPercentageRollout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PercentageRollout",
                table: "FeatureFlags",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PercentageRollout",
                table: "FeatureFlags");
        }
    }
}
