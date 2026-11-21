using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class Badges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BadgeHistorySheetPending",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BadgesBackfilled",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FeaturedBadge",
                table: "UserProfiles",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareBadges",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "BadgeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BadgeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BadgeEvents_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EarnedBadges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BadgeId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    EarnedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SeenAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EarnedBadges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EarnedBadges_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BadgeEvents_UserId_Kind_Date",
                table: "BadgeEvents",
                columns: new[] { "UserId", "Kind", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EarnedBadges_UserId_BadgeId",
                table: "EarnedBadges",
                columns: new[] { "UserId", "BadgeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BadgeEvents");

            migrationBuilder.DropTable(
                name: "EarnedBadges");

            migrationBuilder.DropColumn(
                name: "BadgeHistorySheetPending",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BadgesBackfilled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FeaturedBadge",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ShareBadges",
                table: "UserProfiles");
        }
    }
}
