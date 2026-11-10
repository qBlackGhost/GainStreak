using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Vacations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastAwayCheckAt",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VacationPromptDismissedAt",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vacations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndedEarlyOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    WelcomeShown = table.Column<bool>(type: "INTEGER", nullable: false),
                    VisitingCityId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vacations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vacations_Cities_VisitingCityId",
                        column: x => x.VisitingCityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Vacations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vacations_UserId_StartDate",
                table: "Vacations",
                columns: new[] { "UserId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Vacations_VisitingCityId",
                table: "Vacations",
                column: "VisitingCityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Vacations");

            migrationBuilder.DropColumn(
                name: "LastAwayCheckAt",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "VacationPromptDismissedAt",
                table: "UserProfiles");
        }
    }
}
