using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymApp.Data.Migrations
{
    /// <summary>0.27.1: the app is dark by default. Accounts still on System (the old default; nobody can tell a chosen
    /// System from an untouched one) move to Dark. Data only, no schema change.</summary>
    public partial class DarkThemeDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Users\" SET \"ThemePreference\" = 'Dark' WHERE \"ThemePreference\" = 'System';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo: who was on System before can't be told apart from who picked Dark
        }
    }
}
