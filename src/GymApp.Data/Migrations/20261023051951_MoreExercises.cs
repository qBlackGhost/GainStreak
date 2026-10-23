using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class MoreExercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Exercises",
                columns: new[] { "Id", "MuscleGroup", "Name", "OwnerUserId" },
                values: new object[,]
                {
                    { 66, "Chest", "Machine Chest Press", null },
                    { 67, "Chest", "Pec Deck", null },
                    { 68, "Chest", "Dumbbell Fly", null },
                    { 69, "Back", "Chest-Supported Row", null },
                    { 70, "Back", "Chin-Up", null },
                    { 71, "Back", "Close Grip Lat Pulldown", null },
                    { 72, "Back", "Back Extension", null },
                    { 73, "Shoulders", "Machine Shoulder Press", null },
                    { 74, "Shoulders", "Arnold Press", null },
                    { 75, "Shoulders", "Reverse Pec Deck", null },
                    { 76, "Biceps", "Cable Curl", null },
                    { 77, "Triceps", "Rope Pushdown", null },
                    { 78, "Triceps", "Overhead Cable Triceps Extension", null },
                    { 79, "Quads", "Goblet Squat", null },
                    { 80, "Hamstrings", "Stiff-Leg Deadlift", null },
                    { 81, "Hamstrings", "Dumbbell Romanian Deadlift", null },
                    { 82, "Glutes", "Hip Abduction", null },
                    { 83, "Glutes", "Hip Adduction", null },
                    { 84, "Core", "Hanging Knee Raise", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 68);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 69);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 70);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 71);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 72);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 73);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 74);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 75);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 76);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 77);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 78);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 79);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 80);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 81);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 82);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 83);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 84);
        }
    }
}
