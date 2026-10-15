using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class GymPerCity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Gyms_CityId",
                table: "Gyms");

            migrationBuilder.DropIndex(
                name: "IX_Gyms_OsmType_OsmId",
                table: "Gyms");

            migrationBuilder.CreateIndex(
                name: "IX_Gyms_CityId_OsmType_OsmId",
                table: "Gyms",
                columns: new[] { "CityId", "OsmType", "OsmId" },
                unique: true,
                filter: "\"OsmType\" IS NOT NULL AND \"OsmId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Gyms_CityId_OsmType_OsmId",
                table: "Gyms");

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
        }
    }
}
