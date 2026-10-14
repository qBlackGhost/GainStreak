using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActivityLevel",
                table: "UserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("""
                                 UPDATE UserProfiles SET ActivityLevel =
                                     CASE
                                         WHEN StepsPerDay IS NULL THEN NULL
                                         WHEN StepsPerDay < 7500  THEN 'LessActive'
                                         WHEN StepsPerDay < 12500 THEN 'Active'
                                         ELSE 'VeryActive'
                                     END;
                                 """);

            migrationBuilder.DropColumn(
                name: "StepsPerDay",
                table: "UserProfiles");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StepsPerDay",
                table: "UserProfiles",
                type: "INTEGER",
                nullable: true);

            // Back to a typical step count from the middle of each band
            migrationBuilder.Sql("""
                                 UPDATE UserProfiles SET StepsPerDay =
                                     CASE ActivityLevel
                                         WHEN 'LessActive' THEN 5000
                                         WHEN 'Active'     THEN 10000
                                         WHEN 'VeryActive' THEN 15000
                                         ELSE NULL
                                     END;
                                 """);

            migrationBuilder.DropColumn(
                name: "ActivityLevel",
                table: "UserProfiles");
        }
    }
}
