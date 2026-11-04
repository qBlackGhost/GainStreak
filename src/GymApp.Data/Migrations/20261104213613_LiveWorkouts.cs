using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class LiveWorkouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FilledExercises",
                table: "WorkoutLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalExercises",
                table: "WorkoutLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Verified",
                table: "WorkoutLogs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WorkoutNotificationsAsked",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LiveWorkouts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SplitDayId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActiveAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CurrentExerciseId = table.Column<int>(type: "INTEGER", nullable: true),
                    SkippedExerciseIds = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VerifiedGymId = table.Column<int>(type: "INTEGER", nullable: true),
                    AutoStop = table.Column<string>(type: "TEXT", maxLength: 12, nullable: true),
                    StopNoteSeen = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveWorkouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiveWorkouts_Gyms_VerifiedGymId",
                        column: x => x.VerifiedGymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LiveWorkouts_SplitDays_SplitDayId",
                        column: x => x.SplitDayId,
                        principalTable: "SplitDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LiveWorkouts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveWorkouts_SplitDayId",
                table: "LiveWorkouts",
                column: "SplitDayId");

            migrationBuilder.CreateIndex(
                name: "IX_LiveWorkouts_UserId",
                table: "LiveWorkouts",
                column: "UserId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LiveWorkouts_VerifiedGymId",
                table: "LiveWorkouts",
                column: "VerifiedGymId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveWorkouts");

            migrationBuilder.DropColumn(
                name: "FilledExercises",
                table: "WorkoutLogs");

            migrationBuilder.DropColumn(
                name: "TotalExercises",
                table: "WorkoutLogs");

            migrationBuilder.DropColumn(
                name: "Verified",
                table: "WorkoutLogs");

            migrationBuilder.DropColumn(
                name: "WorkoutNotificationsAsked",
                table: "Users");
        }
    }
}
