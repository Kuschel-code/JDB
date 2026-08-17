using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkMediaTypeUpdatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_works_MediaType_UpdatedAt",
                table: "works",
                columns: new[] { "MediaType", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_works_MediaType_UpdatedAt",
                table: "works");
        }
    }
}
