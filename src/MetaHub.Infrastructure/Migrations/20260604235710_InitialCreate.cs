using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "people",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortName = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    Bios = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_people", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "source_fetch_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    LastCallAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CallsInWindow = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_fetch_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "works",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "text", nullable: false),
                    CanonicalTitle = table.Column<string>(type: "text", nullable: false),
                    OriginalTitle = table.Column<string>(type: "text", nullable: true),
                    ReleaseYear = table.Column<int>(type: "integer", nullable: true),
                    Overview = table.Column<string>(type: "text", nullable: true),
                    OverviewTranslations = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_works", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "book_details",
                columns: table => new
                {
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Isbn13 = table.Column<string>(type: "text", nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    Publisher = table.Column<string>(type: "text", nullable: true),
                    SeriesName = table.Column<string>(type: "text", nullable: true),
                    SeriesIndex = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_book_details", x => x.WorkId);
                    table.ForeignKey(
                        name: "FK_book_details_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "credits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Character = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_credits_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_credits_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_ids",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    ExternalValue = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_ids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_ids_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Lang = table.Column<string>(type: "text", nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_images_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "music_details",
                columns: table => new
                {
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlbumType = table.Column<string>(type: "text", nullable: true),
                    Label = table.Column<string>(type: "text", nullable: true),
                    TrackCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_music_details", x => x.WorkId);
                    table.ForeignKey(
                        name: "FK_music_details_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_payloads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    HttpStatus = table.Column<int>(type: "integer", nullable: false),
                    Body = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_raw_payloads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_raw_payloads_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series_details",
                columns: table => new
                {
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonCount = table.Column<int>(type: "integer", nullable: true),
                    EpisodeCount = table.Column<int>(type: "integer", nullable: true),
                    Network = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series_details", x => x.WorkId);
                    table.ForeignKey(
                        name: "FK_series_details_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_genres",
                columns: table => new
                {
                    WorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenreId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_genres", x => new { x.WorkId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_work_genres_genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_work_genres_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlbumWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscNumber = table.Column<int>(type: "integer", nullable: false),
                    TrackNumber = table.Column<int>(type: "integer", nullable: false),
                    LengthMs = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tracks_music_details_AlbumWorkId",
                        column: x => x.AlbumWorkId,
                        principalTable: "music_details",
                        principalColumn: "WorkId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "episodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeriesWorkId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    AbsoluteNumber = table.Column<int>(type: "integer", nullable: true),
                    AirDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Overview = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_episodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_episodes_series_details_SeriesWorkId",
                        column: x => x.SeriesWorkId,
                        principalTable: "series_details",
                        principalColumn: "WorkId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkId = table.Column<Guid>(type: "uuid", nullable: true),
                    EpisodeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Path = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Ed2kHash = table.Column<string>(type: "text", nullable: true),
                    Crc32 = table.Column<string>(type: "text", nullable: true),
                    AcoustId = table.Column<string>(type: "text", nullable: true),
                    MbRecording = table.Column<string>(type: "text", nullable: true),
                    MovieHash = table.Column<string>(type: "text", nullable: true),
                    IdentifiedBy = table.Column<string>(type: "text", nullable: false),
                    IdentifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Confidence = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_files_episodes_EpisodeId",
                        column: x => x.EpisodeId,
                        principalTable: "episodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_media_files_works_WorkId",
                        column: x => x.WorkId,
                        principalTable: "works",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_book_details_Isbn13",
                table: "book_details",
                column: "Isbn13");

            migrationBuilder.CreateIndex(
                name: "IX_credits_PersonId",
                table: "credits",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_credits_WorkId_PersonId_Role",
                table: "credits",
                columns: new[] { "WorkId", "PersonId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_episodes_SeriesWorkId_SeasonNumber_EpisodeNumber",
                table: "episodes",
                columns: new[] { "SeriesWorkId", "SeasonNumber", "EpisodeNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_external_ids_Source_ExternalValue",
                table: "external_ids",
                columns: new[] { "Source", "ExternalValue" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_ids_WorkId",
                table: "external_ids",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_genres_Name",
                table: "genres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_images_WorkId_Type",
                table: "images",
                columns: new[] { "WorkId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_media_files_AcoustId",
                table: "media_files",
                column: "AcoustId");

            migrationBuilder.CreateIndex(
                name: "IX_media_files_Ed2kHash",
                table: "media_files",
                column: "Ed2kHash");

            migrationBuilder.CreateIndex(
                name: "IX_media_files_EpisodeId",
                table: "media_files",
                column: "EpisodeId");

            migrationBuilder.CreateIndex(
                name: "IX_media_files_Path",
                table: "media_files",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_files_WorkId",
                table: "media_files",
                column: "WorkId");

            migrationBuilder.CreateIndex(
                name: "IX_people_Name",
                table: "people",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_raw_payloads_WorkId_Source",
                table: "raw_payloads",
                columns: new[] { "WorkId", "Source" });

            migrationBuilder.CreateIndex(
                name: "IX_source_fetch_logs_Source",
                table: "source_fetch_logs",
                column: "Source",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tracks_AlbumWorkId_DiscNumber_TrackNumber",
                table: "tracks",
                columns: new[] { "AlbumWorkId", "DiscNumber", "TrackNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_work_genres_GenreId",
                table: "work_genres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_works_CanonicalTitle",
                table: "works",
                column: "CanonicalTitle");

            migrationBuilder.CreateIndex(
                name: "IX_works_MediaType",
                table: "works",
                column: "MediaType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "book_details");

            migrationBuilder.DropTable(
                name: "credits");

            migrationBuilder.DropTable(
                name: "external_ids");

            migrationBuilder.DropTable(
                name: "images");

            migrationBuilder.DropTable(
                name: "media_files");

            migrationBuilder.DropTable(
                name: "raw_payloads");

            migrationBuilder.DropTable(
                name: "source_fetch_logs");

            migrationBuilder.DropTable(
                name: "tracks");

            migrationBuilder.DropTable(
                name: "work_genres");

            migrationBuilder.DropTable(
                name: "people");

            migrationBuilder.DropTable(
                name: "episodes");

            migrationBuilder.DropTable(
                name: "music_details");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "series_details");

            migrationBuilder.DropTable(
                name: "works");
        }
    }
}
