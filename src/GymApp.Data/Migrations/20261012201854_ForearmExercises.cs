using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ForearmExercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 15,
                column: "Name",
                value: "Cable Lateral Raise");

            migrationBuilder.UpdateData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 25,
                column: "Name",
                value: "JM Bench Press");

            migrationBuilder.UpdateData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 26,
                column: "Name",
                value: "Close Grip Dips");

            migrationBuilder.InsertData(
                table: "Exercises",
                columns: new[] { "Id", "MuscleGroup", "Name", "OwnerUserId" },
                values: new object[,]
                {
                    { 48, "Core", "Decline Crunches", null },
                    { 49, "Chest", "Smith Press", null },
                    { 50, "Chest", "Incline Smith Press", null },
                    { 51, "Chest", "Wide Grip Dips", null },
                    { 52, "Back", "T-Bar Row", null },
                    { 53, "Back", "Low Row", null },
                    { 54, "Back", "Close Grip Cable Row", null },
                    { 55, "Back", "Wide Grip Cable Row", null },
                    { 56, "Shoulders", "Dumbbell Lateral Raise", null },
                    { 57, "Biceps", "Preacher Hammer Curl", null },
                    { 58, "Biceps", "Incline Dumbbell Curl", null },
                    { 59, "Triceps", "One Arm Cable Pushdown", null },
                    { 60, "Quads", "Hack Squat", null },
                    { 61, "Forearms", "Forearm Cable Curl", null },
                    { 62, "Forearms", "Forearm Cable Extension", null },
                    { 63, "Forearms", "Reverse Curls", null },
                    { 64, "Forearms", "Wrist Curls", null },
                    { 65, "Forearms", "Reverse Wrist Curls", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.UpdateData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 15,
                column: "Name",
                value: "Lateral Raise");

            migrationBuilder.UpdateData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 25,
                column: "Name",
                value: "Close-Grip Bench Press");

            migrationBuilder.UpdateData(
                table: "Exercises",
                keyColumn: "Id",
                keyValue: 26,
                column: "Name",
                value: "Dips");
        }
    }
}
