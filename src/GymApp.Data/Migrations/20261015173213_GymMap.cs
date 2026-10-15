using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class GymMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing before Phase 9 ever wrote gyms, but any leftover row would have no City to point to
            migrationBuilder.Sql("UPDATE \"WorkoutLogs\" SET \"GymId\" = NULL WHERE \"GymId\" IS NOT NULL;");
            migrationBuilder.Sql("DELETE FROM \"Gyms\";");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkoutLogs_Gyms_GymId",
                table: "WorkoutLogs");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Gyms");

            migrationBuilder.AddColumn<bool>(
                name: "AskForGymAfterCheckIn",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "HomeCityId",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Gyms",
                type: "TEXT",
                nullable: false,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "Longitude",
                table: "Gyms",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<double>(
                name: "Latitude",
                table: "Gyms",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AddColumn<int>(
                name: "CityId",
                table: "Gyms",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Gyms",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<long>(
                name: "OsmId",
                table: "Gyms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsmType",
                table: "Gyms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "Gyms",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OsmType = table.Column<string>(type: "TEXT", nullable: false),
                    OsmId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    CenterLatitude = table.Column<double>(type: "REAL", nullable: false),
                    CenterLongitude = table.Column<double>(type: "REAL", nullable: false),
                    South = table.Column<double>(type: "REAL", nullable: false),
                    West = table.Column<double>(type: "REAL", nullable: false),
                    North = table.Column<double>(type: "REAL", nullable: false),
                    East = table.Column<double>(type: "REAL", nullable: false),
                    GymsFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_HomeCityId",
                table: "UserProfiles",
                column: "HomeCityId");

            migrationBuilder.CreateIndex(
                name: "IX_Gyms_CityId",
                table: "Gyms",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_Gyms_OsmType_OsmId",
                table: "Gyms",
                columns: new[] { "OsmType", "OsmId" },
                unique: true,
                filter: "\"OsmType\" IS NOT NULL AND \"OsmId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Gyms_OwnerUserId_CityId_Name",
                table: "Gyms",
                columns: new[] { "OwnerUserId", "CityId", "Name" },
                unique: true,
                filter: "\"OwnerUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_OsmType_OsmId",
                table: "Cities",
                columns: new[] { "OsmType", "OsmId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Gyms_Cities_CityId",
                table: "Gyms",
                column: "CityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Gyms_Users_OwnerUserId",
                table: "Gyms",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Cities_HomeCityId",
                table: "UserProfiles",
                column: "HomeCityId",
                principalTable: "Cities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkoutLogs_Gyms_GymId",
                table: "WorkoutLogs",
                column: "GymId",
                principalTable: "Gyms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Gyms_Cities_CityId",
                table: "Gyms");

            migrationBuilder.DropForeignKey(
                name: "FK_Gyms_Users_OwnerUserId",
                table: "Gyms");

            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Cities_HomeCityId",
                table: "UserProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkoutLogs_Gyms_GymId",
                table: "WorkoutLogs");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_HomeCityId",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Gyms_CityId",
                table: "Gyms");

            migrationBuilder.DropIndex(
                name: "IX_Gyms_OsmType_OsmId",
                table: "Gyms");

            migrationBuilder.DropIndex(
                name: "IX_Gyms_OwnerUserId_CityId_Name",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "AskForGymAfterCheckIn",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "HomeCityId",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "CityId",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "OsmId",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "OsmType",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Gyms");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Gyms",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldCollation: "NOCASE");

            migrationBuilder.AlterColumn<double>(
                name: "Longitude",
                table: "Gyms",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "Latitude",
                table: "Gyms",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Gyms",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkoutLogs_Gyms_GymId",
                table: "WorkoutLogs",
                column: "GymId",
                principalTable: "Gyms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
