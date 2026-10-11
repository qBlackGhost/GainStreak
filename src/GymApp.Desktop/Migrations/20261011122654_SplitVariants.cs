using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace GymApp.Desktop.Migrations
{
    /// <inheritdoc />
    public partial class SplitVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SelectedVariantId",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SplitStartDay",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SplitVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SplitId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SplitVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SplitVariants_WorkoutSplits_SplitId",
                        column: x => x.SplitId,
                        principalTable: "WorkoutSplits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "SplitVariants",
                columns: new[] { "Id", "IsDefault", "Name", "Pattern", "SplitId" },
                values: new object[,]
                {
                    { 1, true, "6 days / week", "WWWRWWW", 1 },
                    { 2, true, "4 days / week", "WWRWWRR", 2 },
                    { 3, true, "5 days / week", "WWWRWWR", 3 },
                    { 4, false, "2 days / week", "WRRWRRR", 4 },
                    { 5, true, "3 days / week", "WRWRWRR", 4 },
                    { 6, true, "5 days / week", "WWWWWRR", 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_SelectedVariantId",
                table: "Users",
                column: "SelectedVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_SplitVariants_SplitId",
                table: "SplitVariants",
                column: "SplitId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_SplitVariants_SelectedVariantId",
                table: "Users",
                column: "SelectedVariantId",
                principalTable: "SplitVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_SplitVariants_SelectedVariantId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "SplitVariants");

            migrationBuilder.DropIndex(
                name: "IX_Users_SelectedVariantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SelectedVariantId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SplitStartDay",
                table: "Users");
        }
    }
}
