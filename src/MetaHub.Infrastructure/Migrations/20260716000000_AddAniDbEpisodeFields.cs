using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAniDbEpisodeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AniDbEpisodeId",
                table: "episodes",
                type: "integer",
                nullable: true);

            // Existing rows predate AniDB kinds and are regular episodes; the enum is stored
            // as text, so an empty default would fail to parse on read.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "episodes",
                type: "text",
                nullable: false,
                defaultValue: "Regular");

            migrationBuilder.AddColumn<string>(
                name: "RawEpno",
                table: "episodes",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_episodes_AniDbEpisodeId",
                table: "episodes",
                column: "AniDbEpisodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_episodes_AniDbEpisodeId",
                table: "episodes");

            migrationBuilder.DropColumn(
                name: "AniDbEpisodeId",
                table: "episodes");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "episodes");

            migrationBuilder.DropColumn(
                name: "RawEpno",
                table: "episodes");
        }
    }
}
