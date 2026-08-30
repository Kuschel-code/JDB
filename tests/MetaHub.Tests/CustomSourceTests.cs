using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Enrichment.CustomSources;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers the user-hosted metadata databases: the one-line configuration format, reading a
/// folder of JSON/NFO files, and how the results reach the merge pipeline.
/// </summary>
public class CustomSourceTests
{
    // --- configuration format ---

    [Fact]
    public void Parses_name_location_priority_and_key()
    {
        Assert.True(CustomSource.TryParse("Home API | https://meta.lan/api | 3 | s3cret", out var source));
        Assert.Equal("Home API", source.Name);
        Assert.Equal(CustomSourceKind.Http, source.Kind);
        Assert.Equal("https://meta.lan/api", source.Location);
        Assert.Equal(3, source.Priority);
        Assert.Equal("s3cret", source.ApiKey);
    }

    [Fact]
    public void Derives_kind_and_name_from_a_bare_location()
    {
        Assert.True(CustomSource.TryParse("/mnt/nas/metadata", out var folder));
        Assert.Equal(CustomSourceKind.Folder, folder.Kind);
        Assert.Equal("metadata", folder.Name);
        Assert.Equal(CustomSource.DefaultPriority, folder.Priority);

        Assert.True(CustomSource.TryParse("https://meta.lan/db", out var http));
        Assert.Equal(CustomSourceKind.Http, http.Kind);
        Assert.Equal("meta.lan", http.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# a comment")]
    [InlineData("Name only |")]
    public void Skips_blank_comment_and_incomplete_lines(string line)
        => Assert.False(CustomSource.TryParse(line, out _));

    [Theory]
    [InlineData("relative/path")]
    [InlineData("My NAS | relative/path")]
    [InlineData("ftp://nas.lan/share")]
    public void Rejects_locations_that_are_not_absolute(string line)
        => Assert.False(CustomSource.TryParse(line, out _));

    [Fact]
    public void A_trailing_separator_still_yields_the_location()
    {
        // "https://x.lan/api | " means the URL with no display name, not a name with no location.
        Assert.True(CustomSource.TryParse("https://x.lan/api | ", out var source));
        Assert.Equal("https://x.lan/api", source.Location);
        Assert.Equal(CustomSourceKind.Http, source.Kind);
    }

    [Fact]
    public void ParseAll_keeps_only_usable_lines()
    {
        var sources = CustomSource.ParseAll(
            new[] { "# header", "", "My NAS | /srv/db", "https://x.lan/api | ", "not-absolute" });
        Assert.Equal(2, sources.Count);
        Assert.Equal("My NAS", sources[0].Name);
        Assert.Equal("https://x.lan/api", sources[1].Location);
    }

    // --- document parsing ---

    [Fact]
    public void Reads_a_json_document_with_images_and_people()
    {
        const string json = """
        {
          "title": "My Show", "originalTitle": "Meine Serie", "year": 2021,
          "overview": "An overview.", "overviews": { "de": "Eine Beschreibung." },
          "titles": { "ja": "マイショー" },
          "status": "Finished", "genres": ["Action", "Drama"],
          "episodeCount": 12, "network": "My Studio",
          "poster": "poster.jpg",
          "images": [{ "type": "backdrop", "url": "https://cdn.lan/bd.jpg", "lang": "de", "width": 1920, "height": 1080 }],
          "people": [{ "name": "Jane Doe", "role": "Actor", "character": "Hero", "order": 0 }]
        }
        """;

        var data = CustomSourceParser.ParseJson(json, "/srv/db/My Show");

        Assert.Equal(ExternalIdSource.Custom, data.Source);
        Assert.Equal("My Show", data.CanonicalTitle);
        Assert.Equal("Meine Serie", data.OriginalTitle);
        Assert.Equal(2021, data.ReleaseYear);
        Assert.Equal(WorkStatus.Finished, data.Status);
        Assert.Equal("Eine Beschreibung.", data.OverviewTranslations["de"]);
        Assert.Equal("マイショー", data.TitleTranslations["ja"]);
        Assert.Equal(new[] { "Action", "Drama" }, data.Genres);
        Assert.Equal(12, data.EpisodeCount);

        // Relative artwork resolves against the document's folder; absolute URLs pass through.
        var poster = Assert.Single(data.Images, i => i.Type == ImageType.Poster);
        Assert.Equal(Path.GetFullPath("/srv/db/My Show/poster.jpg"), poster.Url);
        var backdrop = Assert.Single(data.Images, i => i.Type == ImageType.Backdrop);
        Assert.Equal("https://cdn.lan/bd.jpg", backdrop.Url);

        var credit = Assert.Single(data.Credits);
        Assert.Equal("Jane Doe", credit.Name);
        Assert.Equal(CreditRole.Actor, credit.Role);
        Assert.Equal("Hero", credit.Character);
    }

    [Fact]
    public void Reads_a_kodi_style_nfo()
    {
        const string nfo = """
        <?xml version="1.0" encoding="utf-8"?>
        <tvshow>
          <title>My Show</title>
          <originaltitle>Meine Serie</originaltitle>
          <plot>An overview.</plot>
          <premiered>2021-04-01</premiered>
          <status>Ended</status>
          <studio>My Studio</studio>
          <genre>Action</genre>
          <genre>Drama</genre>
          <thumb aspect="poster">poster.jpg</thumb>
          <fanart><thumb>fanart.jpg</thumb></fanart>
          <actor><name>Jane Doe</name><role>Hero</role><order>0</order></actor>
          <director>Max Mustermann</director>
        </tvshow>
        """;

        var data = CustomSourceParser.ParseNfo(nfo, "/srv/db/My Show");

        Assert.Equal("My Show", data.CanonicalTitle);
        Assert.Equal("Meine Serie", data.OriginalTitle);
        Assert.Equal(2021, data.ReleaseYear);
        Assert.Equal(WorkStatus.Finished, data.Status);
        Assert.Equal("My Studio", data.Network);
        Assert.Equal(new[] { "Action", "Drama" }, data.Genres);
        Assert.Contains(data.Images, i => i.Type == ImageType.Poster);
        Assert.Contains(data.Images, i => i.Type == ImageType.Backdrop);
        Assert.Contains(data.Credits, c => c.Role == CreditRole.Actor && c.Name == "Jane Doe");
        Assert.Contains(data.Credits, c => c.Role == CreditRole.Director && c.Name == "Max Mustermann");
    }

    // --- folder source end to end ---

    private static CustomSourceService NewService(params CustomSource[] sources)
    {
        var provider = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var options = Options.Create(new EnrichmentOptions { CustomSources = sources.ToList() });
        return new CustomSourceService(
            provider.GetRequiredService<IHttpClientFactory>(), options,
            NullLogger<CustomSourceService>.Instance);
    }

    [Fact]
    public async Task Reads_metadata_from_a_folder_matched_by_title()
    {
        var root = Path.Combine(Path.GetTempPath(), $"metahub-custom-{Guid.NewGuid():N}");
        // The folder is punctuated differently than the stored title — matching ignores that.
        var showDir = Path.Combine(root, "The Disastrous Life of Saiki K");
        Directory.CreateDirectory(showDir);
        await File.WriteAllTextAsync(Path.Combine(showDir, "metahub.json"),
            """{ "overview": "From my own database.", "year": 2016, "poster": "poster.jpg" }""");
        await File.WriteAllTextAsync(Path.Combine(showDir, "poster.jpg"), "not-a-real-image");

        try
        {
            var service = NewService(new CustomSource { Name = "NAS", Kind = CustomSourceKind.Folder, Location = root });
            var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "The Disastrous Life of Saiki K." };

            var results = await service.FetchAsync(work, CancellationToken.None);

            var (priority, data) = Assert.Single(results);
            Assert.Equal(CustomSource.DefaultPriority, priority);
            Assert.Equal("From my own database.", data.Overview);
            Assert.Equal(2016, data.ReleaseYear);
            Assert.Equal(Path.Combine(showDir, "poster.jpg"), Assert.Single(data.Images).Url);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Returns_nothing_when_the_title_is_not_in_the_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"metahub-custom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Some Other Show"));
        try
        {
            var service = NewService(new CustomSource { Kind = CustomSourceKind.Folder, Location = root });
            var work = new Work { MediaType = MediaType.Anime, CanonicalTitle = "My Show" };

            Assert.Empty(await service.FetchAsync(work, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task A_missing_folder_is_reported_but_never_throws()
    {
        var service = NewService(new CustomSource
        {
            Name = "gone",
            Kind = CustomSourceKind.Folder,
            Location = Path.Combine(Path.GetTempPath(), $"metahub-missing-{Guid.NewGuid():N}")
        });

        Assert.Empty(await service.FetchAsync(
            new Work { CanonicalTitle = "My Show" }, CancellationToken.None));
    }

    [Fact]
    public async Task Matches_a_folder_named_after_a_translated_title()
    {
        var root = Path.Combine(Path.GetTempPath(), $"metahub-custom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Even If the World Ends Tomorrow"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "Even If the World Ends Tomorrow", "metahub.json"),
            """{ "overview": "Matched via the English title." }""");

        try
        {
            var service = NewService(new CustomSource { Kind = CustomSourceKind.Folder, Location = root });
            var work = new Work { CanonicalTitle = "Ashita Sekai ga Owaru to Shitemo" };
            work.TitleTranslations["en"] = "Even If the World Ends Tomorrow";

            var (_, data) = Assert.Single(await service.FetchAsync(work, CancellationToken.None));
            Assert.Equal("Matched via the English title.", data.Overview);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
