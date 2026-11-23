using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class MacroPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MacroBasisWeightKg",
                table: "Users",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MacroMode",
                table: "Users",
                type: "TEXT",
                maxLength: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MacroBasisWeightKg",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MacroMode",
                table: "Users");
        }
    }
}
