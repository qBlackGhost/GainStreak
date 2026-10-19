using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGymSharingAndHomeGym : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HomeGymId",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareGymCheckIns",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_HomeGymId",
                table: "UserProfiles",
                column: "HomeGymId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Gyms_HomeGymId",
                table: "UserProfiles",
                column: "HomeGymId",
                principalTable: "Gyms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Gyms_HomeGymId",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_HomeGymId",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "HomeGymId",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ShareGymCheckIns",
                table: "UserProfiles");
        }
    }
}
