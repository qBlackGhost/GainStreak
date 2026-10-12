using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Exercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Exercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false, collation: "NOCASE"),
                    MuscleGroup = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exercises_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExerciseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    WeightKg = table.Column<double>(type: "REAL", nullable: false),
                    Reps = table.Column<int>(type: "INTEGER", nullable: false),
                    OneRepMaxKg = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrAdjustments_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PrAdjustments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SetLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExerciseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SetNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Reps = table.Column<int>(type: "INTEGER", nullable: false),
                    WeightKg = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetLogs_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDayExercises",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SplitDayId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExerciseId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    WarmupSets = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkingSets = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDayExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserDayExercises_Exercises_ExerciseId",
                        column: x => x.ExerciseId,
                        principalTable: "Exercises",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDayExercises_SplitDays_SplitDayId",
                        column: x => x.SplitDayId,
                        principalTable: "SplitDays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDayExercises_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Exercises",
                columns: new[] { "Id", "MuscleGroup", "Name", "OwnerUserId" },
                values: new object[,]
                {
                    { 1, "Chest", "Bench Press", null },
                    { 2, "Chest", "Incline Bench Press", null },
                    { 3, "Chest", "Dumbbell Bench Press", null },
                    { 4, "Chest", "Incline Dumbbell Press", null },
                    { 5, "Chest", "Cable Fly", null },
                    { 6, "Chest", "Push-Up", null },
                    { 7, "Back", "Pull-Up", null },
                    { 8, "Back", "Lat Pulldown", null },
                    { 9, "Back", "Barbell Row", null },
                    { 10, "Back", "Seated Cable Row", null },
                    { 11, "Back", "One-Arm Dumbbell Row", null },
                    { 12, "Back", "Deadlift", null },
                    { 13, "Shoulders", "Overhead Press", null },
                    { 14, "Shoulders", "Dumbbell Shoulder Press", null },
                    { 15, "Shoulders", "Lateral Raise", null },
                    { 16, "Shoulders", "Rear Delt Fly", null },
                    { 17, "Shoulders", "Face Pull", null },
                    { 18, "Biceps", "Barbell Curl", null },
                    { 19, "Biceps", "Dumbbell Curl", null },
                    { 20, "Biceps", "Hammer Curl", null },
                    { 21, "Biceps", "Preacher Curl", null },
                    { 22, "Triceps", "Triceps Pushdown", null },
                    { 23, "Triceps", "Skull Crusher", null },
                    { 24, "Triceps", "Overhead Triceps Extension", null },
                    { 25, "Triceps", "Close-Grip Bench Press", null },
                    { 26, "Triceps", "Dips", null },
                    { 27, "Quads", "Squat", null },
                    { 28, "Quads", "Leg Press", null },
                    { 29, "Quads", "Leg Extension", null },
                    { 30, "Quads", "Bulgarian Split Squat", null },
                    { 31, "Quads", "Front Squat", null },
                    { 32, "Hamstrings", "Romanian Deadlift", null },
                    { 33, "Hamstrings", "Lying Leg Curl", null },
                    { 34, "Hamstrings", "Seated Leg Curl", null },
                    { 35, "Hamstrings", "Good Morning", null },
                    { 36, "Glutes", "Hip Thrust", null },
                    { 37, "Glutes", "Glute Bridge", null },
                    { 38, "Glutes", "Cable Kickback", null },
                    { 39, "Glutes", "Walking Lunge", null },
                    { 40, "Calves", "Standing Calf Raise", null },
                    { 41, "Calves", "Seated Calf Raise", null },
                    { 42, "Calves", "Leg Press Calf Raise", null },
                    { 43, "Core", "Plank", null },
                    { 44, "Core", "Hanging Leg Raise", null },
                    { 45, "Core", "Cable Crunch", null },
                    { 46, "Core", "Ab Wheel Rollout", null },
                    { 47, "Core", "Russian Twist", null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Exercises_OwnerUserId_Name",
                table: "Exercises",
                columns: new[] { "OwnerUserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrAdjustments_ExerciseId",
                table: "PrAdjustments",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_PrAdjustments_UserId_ExerciseId_Date",
                table: "PrAdjustments",
                columns: new[] { "UserId", "ExerciseId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SetLogs_ExerciseId",
                table: "SetLogs",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_SetLogs_UserId_ExerciseId_Date_SetNumber",
                table: "SetLogs",
                columns: new[] { "UserId", "ExerciseId", "Date", "SetNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDayExercises_ExerciseId",
                table: "UserDayExercises",
                column: "ExerciseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDayExercises_SplitDayId",
                table: "UserDayExercises",
                column: "SplitDayId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDayExercises_UserId_SplitDayId_ExerciseId",
                table: "UserDayExercises",
                columns: new[] { "UserId", "SplitDayId", "ExerciseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrAdjustments");

            migrationBuilder.DropTable(
                name: "SetLogs");

            migrationBuilder.DropTable(
                name: "UserDayExercises");

            migrationBuilder.DropTable(
                name: "Exercises");
        }
    }
}
