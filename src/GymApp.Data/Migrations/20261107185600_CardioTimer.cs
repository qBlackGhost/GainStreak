using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class CardioTimer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardioSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    GymId = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RunningSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CountedSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAtGymAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PauseReason = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SavedMinutes = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardioSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardioSessions_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CardioSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardioSessions_GymId",
                table: "CardioSessions",
                column: "GymId");

            migrationBuilder.CreateIndex(
                name: "IX_CardioSessions_UserId",
                table: "CardioSessions",
                column: "UserId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardioSessions");
        }
    }
}
