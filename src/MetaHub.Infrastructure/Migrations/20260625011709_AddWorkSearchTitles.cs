using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkSearchTitles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchTitles",
                table: "works",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TitleTranslations",
                table: "works",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchTitles",
                table: "works");

            migrationBuilder.DropColumn(
                name: "TitleTranslations",
                table: "works");
        }
    }
}
