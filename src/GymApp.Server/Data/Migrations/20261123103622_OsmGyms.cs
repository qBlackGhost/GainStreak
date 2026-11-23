using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class OsmGyms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OsmGymCountries",
                columns: table => new
                {
                    Country = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    South = table.Column<double>(type: "double precision", nullable: false),
                    West = table.Column<double>(type: "double precision", nullable: false),
                    North = table.Column<double>(type: "double precision", nullable: false),
                    East = table.Column<double>(type: "double precision", nullable: false),
                    DataAsOf = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GymCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OsmGymCountries", x => x.Country);
                });

            migrationBuilder.CreateTable(
                name: "OsmGyms",
                columns: table => new
                {
                    OsmType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OsmId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Country = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OsmGyms", x => new { x.OsmType, x.OsmId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_OsmGyms_Country",
                table: "OsmGyms",
                column: "Country");

            migrationBuilder.CreateIndex(
                name: "IX_OsmGyms_Latitude_Longitude",
                table: "OsmGyms",
                columns: new[] { "Latitude", "Longitude" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OsmGymCountries");

            migrationBuilder.DropTable(
                name: "OsmGyms");
        }
    }
}
