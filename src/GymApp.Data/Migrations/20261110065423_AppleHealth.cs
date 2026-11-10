using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppleHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FromHealth",
                table: "WeightLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HealthEnabled",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "HealthEnabledAt",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthProfileDismissed",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HealthStepsSuggestionSeen",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "HealthWeightAnchor",
                table: "UserProfiles",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromHealth",
                table: "WeightLogs");

            migrationBuilder.DropColumn(
                name: "HealthEnabled",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "HealthEnabledAt",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "HealthProfileDismissed",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "HealthStepsSuggestionSeen",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "HealthWeightAnchor",
                table: "UserProfiles");
        }
    }
}
