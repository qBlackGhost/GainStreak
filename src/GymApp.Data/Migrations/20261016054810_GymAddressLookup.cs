using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class GymAddressLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AddressLookedUpAt",
                table: "Gyms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookedUpAddress",
                table: "Gyms",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLookedUpAt",
                table: "Gyms");

            migrationBuilder.DropColumn(
                name: "LookedUpAddress",
                table: "Gyms");
        }
    }
}
