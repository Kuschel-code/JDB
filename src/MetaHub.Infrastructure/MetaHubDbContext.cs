using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Infrastructure;

/// <summary>
/// EF Core database context for MetaHub. Targets PostgreSQL and uses JSONB for
/// localized text and raw provider payloads.
/// </summary>
public class MetaHubDbContext : DbContext
{
    public MetaHubDbContext(DbContextOptions<MetaHubDbContext> options) : base(options)
    {
    }

    public DbSet<Work> Works => Set<Work>();
    public DbSet<ExternalId> ExternalIds => Set<ExternalId>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Credit> Credits => Set<Credit>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<WorkGenre> WorkGenres => Set<WorkGenre>();
    public DbSet<SeriesDetail> SeriesDetails => Set<SeriesDetail>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<MusicDetail> MusicDetails => Set<MusicDetail>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<BookDetail> BookDetails => Set<BookDetail>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<RawPayload> RawPayloads => Set<RawPayload>();
    public DbSet<SourceFetchLog> SourceFetchLogs => Set<SourceFetchLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // PostgreSQL stores JSON columns as jsonb; SQLite (embedded mode) stores them as TEXT.
        var useJsonb = Database.IsNpgsql();

        // Serialize string-keyed dictionaries to JSON text and back.
        var dictConverter = new ValueConverter<Dictionary<string, string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, (JsonSerializerOptions?)null)
                 ?? new Dictionary<string, string>());

        var dictComparer = new ValueComparer<Dictionary<string, string>>(
            (a, c) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
                      == JsonSerializer.Serialize(c, (JsonSerializerOptions?)null),
            v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(
                     JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)
                 ?? new Dictionary<string, string>());

        // Persist ExternalIdSource as text so adding providers does not require a schema change.
        var sourceConverter = new EnumToStringConverter<ExternalIdSource>();

        b.Entity<Work>(e =>
        {
            e.ToTable("works");
            e.HasKey(x => x.Id);
            e.Property(x => x.MediaType).HasConversion<string>();
            e.Property(x => x.Status).HasConversion<string>();
            e.Property(x => x.CanonicalTitle).IsRequired();
            var ot = e.Property(x => x.OverviewTranslations);
            ot.HasConversion(dictConverter);
            ot.Metadata.SetValueComparer(dictComparer);
            if (useJsonb) ot.HasColumnType("jsonb");
            e.HasIndex(x => x.MediaType);
            e.HasIndex(x => x.CanonicalTitle);
        });

        b.Entity<ExternalId>(e =>
        {
            e.ToTable("external_ids");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasConversion(sourceConverter);
            e.Property(x => x.ExternalValue).IsRequired();
            e.HasIndex(x => new { x.Source, x.ExternalValue }).IsUnique();
            e.HasOne(x => x.Work)
                .WithMany(w => w.ExternalIds)
                .HasForeignKey(x => x.WorkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Person>(e =>
        {
            e.ToTable("people");
            e.HasKey(x => x.Id);
            var bios = e.Property(x => x.Bios);
            bios.HasConversion(dictConverter);
            bios.Metadata.SetValueComparer(dictComparer);
            if (useJsonb) bios.HasColumnType("jsonb");
            e.HasIndex(x => x.Name);
        });

        b.Entity<Credit>(e =>
        {
            e.ToTable("credits");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasConversion<string>();
            e.HasOne(x => x.Work).WithMany(w => w.Credits)
                .HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Person).WithMany(p => p.Credits)
                .HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorkId, x.PersonId, x.Role });
        });

        b.Entity<Image>(e =>
        {
            e.ToTable("images");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>();
            e.HasOne(x => x.Work).WithMany(w => w.Images)
                .HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorkId, x.Type });
        });

        b.Entity<Genre>(e =>
        {
            e.ToTable("genres");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<WorkGenre>(e =>
        {
            e.ToTable("work_genres");
            e.HasKey(x => new { x.WorkId, x.GenreId });
            e.HasOne(x => x.Work).WithMany(w => w.WorkGenres)
                .HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Genre).WithMany(g => g.WorkGenres)
                .HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<SeriesDetail>(e =>
        {
            e.ToTable("series_details");
            e.HasKey(x => x.WorkId);
            e.HasOne(x => x.Work).WithOne(w => w.SeriesDetail)
                .HasForeignKey<SeriesDetail>(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Episode>(e =>
        {
            e.ToTable("episodes");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Series).WithMany(s => s.Episodes)
                .HasForeignKey(x => x.SeriesWorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SeriesWorkId, x.SeasonNumber, x.EpisodeNumber });
        });

        b.Entity<MusicDetail>(e =>
        {
            e.ToTable("music_details");
            e.HasKey(x => x.WorkId);
            e.HasOne(x => x.Work).WithOne(w => w.MusicDetail)
                .HasForeignKey<MusicDetail>(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Track>(e =>
        {
            e.ToTable("tracks");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Album).WithMany(m => m.Tracks)
                .HasForeignKey(x => x.AlbumWorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AlbumWorkId, x.DiscNumber, x.TrackNumber });
        });

        b.Entity<BookDetail>(e =>
        {
            e.ToTable("book_details");
            e.HasKey(x => x.WorkId);
            e.HasOne(x => x.Work).WithOne(w => w.BookDetail)
                .HasForeignKey<BookDetail>(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Isbn13);
        });

        b.Entity<MediaFile>(e =>
        {
            e.ToTable("media_files");
            e.HasKey(x => x.Id);
            e.Property(x => x.IdentifiedBy).HasConversion<string>();
            e.HasOne(x => x.Work).WithMany(w => w.MediaFiles)
                .HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Episode).WithMany(ep => ep.MediaFiles)
                .HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Path).IsUnique();
            e.HasIndex(x => x.Ed2kHash);
            e.HasIndex(x => x.AcoustId);
        });

        b.Entity<RawPayload>(e =>
        {
            e.ToTable("raw_payloads");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasConversion(sourceConverter);
            if (useJsonb) e.Property(x => x.Body).HasColumnType("jsonb");
            e.HasOne(x => x.Work).WithMany(w => w.RawPayloads)
                .HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.WorkId, x.Source });
        });

        b.Entity<SourceFetchLog>(e =>
        {
            e.ToTable("source_fetch_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasConversion(sourceConverter);
            e.HasIndex(x => x.Source).IsUnique();
        });
    }
}
