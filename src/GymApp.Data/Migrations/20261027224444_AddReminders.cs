using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing users: reminders off, both kinds ready for when they turn them on, training at 18:00 (16g)
            migrationBuilder.AddColumn<bool>(
                name: "MissedDayReminderEnabled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RemindersEnabled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TrainingReminderEnabled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "TrainingReminderTime",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new TimeOnly(18, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MissedDayReminderEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RemindersEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TrainingReminderEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TrainingReminderTime",
                table: "Users");
        }
    }
}
