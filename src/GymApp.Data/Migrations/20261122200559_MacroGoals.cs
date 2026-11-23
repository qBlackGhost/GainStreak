using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class MacroGoals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CarbsGoalGrams",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FatGoalGrams",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProteinGoalGrams",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CarbsGoalGrams",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FatGoalGrams",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProteinGoalGrams",
                table: "Users");
        }
    }
}
