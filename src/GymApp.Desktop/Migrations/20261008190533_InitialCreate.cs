using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymApp.Desktop.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Gyms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gyms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CalorieLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CaloriesConsumed = table.Column<int>(type: "INTEGER", nullable: false),
                    GoalAtTime = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalorieLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SplitDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SplitId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitDays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SelectedSplitId = table.Column<int>(type: "INTEGER", nullable: true),
                    SelectedCity = table.Column<string>(type: "TEXT", nullable: true),
                    Points = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    LongestStreak = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyCalorieGoal = table.Column<int>(type: "INTEGER", nullable: true),
                    GoalType = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SplitDayId = table.Column<int>(type: "INTEGER", nullable: true),
                    GymId = table.Column<int>(type: "INTEGER", nullable: true),
                    PointsAwarded = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_Gyms_GymId",
                        column: x => x.GymId,
                        principalTable: "Gyms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_SplitDays_SplitDayId",
                        column: x => x.SplitDayId,
                        principalTable: "SplitDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkoutLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkoutSplits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsRecommended = table.Column<bool>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkoutSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkoutSplits_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "WorkoutSplits",
                columns: new[] { "Id", "Description", "IsRecommended", "Name", "OwnerUserId" },
                values: new object[,]
                {
                    { 1, null, true, "Push / Pull / Legs", null },
                    { 2, null, true, "Upper / Lower", null },
                    { 3, null, true, "PPL + Upper / Lower", null },
                    { 4, null, false, "Full Body", null },
                    { 5, null, false, "Bro Split", null }
                });

            migrationBuilder.InsertData(
                table: "SplitDays",
                columns: new[] { "Id", "Name", "Order", "SplitId" },
                values: new object[,]
                {
                    { 1, "Push", 1, 1 },
                    { 2, "Pull", 2, 1 },
                    { 3, "Legs", 3, 1 },
                    { 4, "Upper", 1, 2 },
                    { 5, "Lower", 2, 2 },
                    { 6, "Push", 1, 3 },
                    { 7, "Pull", 2, 3 },
                    { 8, "Legs", 3, 3 },
                    { 9, "Upper", 4, 3 },
                    { 10, "Lower", 5, 3 },
                    { 11, "Full Body", 1, 4 },
                    { 12, "Chest", 1, 5 },
                    { 13, "Back", 2, 5 },
                    { 14, "Shoulders", 3, 5 },
                    { 15, "Legs", 4, 5 },
                    { 16, "Arms", 5, 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalorieLogs_UserId_Date",
                table: "CalorieLogs",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SplitDays_SplitId",
                table: "SplitDays",
                column: "SplitId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_SelectedSplitId",
                table: "Users",
                column: "SelectedSplitId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_GymId",
                table: "WorkoutLogs",
                column: "GymId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_SplitDayId",
                table: "WorkoutLogs",
                column: "SplitDayId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutLogs_UserId_Date",
                table: "WorkoutLogs",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkoutSplits_OwnerUserId",
                table: "WorkoutSplits",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CalorieLogs_Users_UserId",
                table: "CalorieLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SplitDays_WorkoutSplits_SplitId",
                table: "SplitDays",
                column: "SplitId",
                principalTable: "WorkoutSplits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_WorkoutSplits_SelectedSplitId",
                table: "Users",
                column: "SelectedSplitId",
                principalTable: "WorkoutSplits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkoutSplits_Users_OwnerUserId",
                table: "WorkoutSplits");

            migrationBuilder.DropTable(
                name: "CalorieLogs");

            migrationBuilder.DropTable(
                name: "WorkoutLogs");

            migrationBuilder.DropTable(
                name: "Gyms");

            migrationBuilder.DropTable(
                name: "SplitDays");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WorkoutSplits");
        }
    }
}
