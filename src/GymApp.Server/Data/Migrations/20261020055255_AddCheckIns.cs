using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckIns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CheckIns",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    GymOsmType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    GymOsmId = table.Column<long>(type: "bigint", nullable: true),
                    GymName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CityOsmType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CityOsmId = table.Column<long>(type: "bigint", nullable: false),
                    CityName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CheckIns", x => new { x.UserId, x.Date });
                    table.CheckConstraint("CK_CheckIns_CustomGymHasNoPosition", "\"GymOsmId\" IS NOT NULL OR (\"Latitude\" IS NULL AND \"Longitude\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_CheckIns_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CheckIns_Date",
                table: "CheckIns",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CheckIns");
        }
    }
}
