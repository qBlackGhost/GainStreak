using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitDayWeekday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Weekday",
                table: "SplitDays",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 1,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 2,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 3,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 4,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 5,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 6,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 7,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 8,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 9,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 10,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 11,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 12,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 13,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 14,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 15,
                column: "Weekday",
                value: null);

            migrationBuilder.UpdateData(
                table: "SplitDays",
                keyColumn: "Id",
                keyValue: 16,
                column: "Weekday",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Weekday",
                table: "SplitDays");
        }
    }
}
