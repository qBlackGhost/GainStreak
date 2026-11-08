using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class PickedRestDayExercises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExerciseIds",
                table: "WorkoutLogs",
                type: "TEXT",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SplitDayId",
                table: "LiveWorkouts",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "ExerciseIds",
                table: "LiveWorkouts",
                type: "TEXT",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExerciseIds",
                table: "WorkoutLogs");

            migrationBuilder.DropColumn(
                name: "ExerciseIds",
                table: "LiveWorkouts");

            migrationBuilder.AlterColumn<int>(
                name: "SplitDayId",
                table: "LiveWorkouts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
