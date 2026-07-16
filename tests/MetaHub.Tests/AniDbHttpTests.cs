using System.Text.Json;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;
using MetaHub.Enrichment;
using MetaHub.Enrichment.Providers;
using MetaHub.Identification.AniDb;
using Xunit;

namespace MetaHub.Tests;

public class AniDbHttpTests
{
    private const string SampleAnimeXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <anime id="1" restricted="false">
          <type>TV Series</type>
          <episodecount>26</episodecount>
          <startdate>1998-04-03</startdate>
          <enddate>1999-04-24</enddate>
          <titles>
            <title xml:lang="x-jat" type="main">Cowboy Bebop</title>
            <title xml:lang="en" type="official">Cowboy Bebop</title>
            <title xml:lang="ja" type="official">カウボーイビバップ</title>
            <title xml:lang="de" type="synonym">Cowboy Bebop - Deutsch</title>
            <title xml:lang="de" type="official">Cowboy Bebop DE</title>
          </titles>
          <description>In the year 2071, bounty hunters roam the solar system.</description>
          <picture>12345.jpg</picture>
          <ratings>
            <permanent count="5000">8.42</permanent>
          </ratings>
          <tags>
            <tag><name>space</name></tag>
            <tag><name>bounty hunter</name></tag>
          </tags>
          <episodes>
            <episode id="100">
              <epno type="1">1</epno>
              <title xml:lang="en">Asteroid Blues</title>
              <title xml:lang="ja">アステロイド・ブルース</title>
              <airdate>1998-04-03</airdate>
              <length>24</length>
            </episode>
            <episode id="101">
              <epno type="1">2</epno>
              <title xml:lang="en">Stray Dog Strut</title>
              <airdate>1998-04-10</airdate>
              <length>24</length>
            </episode>
            <episode id="200">
              <epno type="2">S1</epno>
              <title xml:lang="en">Special Interview</title>
            </episode>
            <episode id="300">
              <epno type="3">C1</epno>
              <title xml:lang="en">Opening Credits</title>
            </episode>
          </episodes>
          <characters>
            <character>
              <name>Spike Spiegel</name>
              <characterimage><normal>spike.jpg</normal></characterimage>
              <seiyuu picture="koichi.jpg">Yamadera Koichi</seiyuu>
            </character>
            <character>
              <name>Faye Valentine</name>
              <seiyuu>Hayashibara Megumi</seiyuu>
            </character>
          </characters>
          <relatedanime>
            <anime id="2" type="Sequel">Cowboy Bebop: Tengoku no Tobira</anime>
          </relatedanime>
        </anime>
        """;

    // --- AniDbAnimeParser tests ---

    [Fact]
    public void Parser_extracts_all_fields_from_anime_xml()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);

        Assert.Equal(1, anime.Aid);
        Assert.Equal("Cowboy Bebop", anime.MainTitle);
        Assert.Equal("カウボーイビバップ", anime.OriginalTitle);
        Assert.Equal("TV Series", anime.Type);
        Assert.Equal(26, anime.EpisodeCount);
        Assert.Equal("1998-04-03", anime.StartDate);
        Assert.Equal("1999-04-24", anime.EndDate);
        Assert.Equal(8.42, anime.Rating);
        Assert.Contains("In the year 2071", anime.Description!);
        Assert.Equal("https://cdn.anidb.net/images/main/12345.jpg", anime.PictureUrl);
    }

    [Fact]
    public void Parser_extracts_titles_with_lang_and_type()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);

        Assert.Equal(5, anime.Titles.Count);
        Assert.Contains(anime.Titles, t => t.Lang == "x-jat" && t.Type == "main" && t.Text == "Cowboy Bebop");
        Assert.Contains(anime.Titles, t => t.Lang == "ja" && t.Type == "official" && t.Text == "カウボーイビバップ");
        Assert.Contains(anime.Titles, t => t.Lang == "de" && t.Type == "synonym");
        Assert.Contains(anime.Titles, t => t.Lang == "de" && t.Type == "official");
    }

    [Fact]
    public void Parser_extracts_episodes_with_kind_and_number()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);

        Assert.Equal(4, anime.Episodes.Count);

        var ep1 = anime.Episodes.First(e => e.Id == 100);
        Assert.Equal("1", ep1.RawEpno);
        Assert.Equal(1, ep1.EpnoType);
        Assert.Equal(EpisodeKind.Regular, ep1.Kind);
        Assert.Equal(1, ep1.ParsedNumber);
        Assert.Equal("Asteroid Blues", ep1.TitleEn);
        Assert.Equal("アステロイド・ブルース", ep1.Title);
        Assert.Equal(new DateOnly(1998, 4, 3), ep1.AirDate);
        Assert.Equal(24, ep1.Length);

        var special = anime.Episodes.First(e => e.Id == 200);
        Assert.Equal("S1", special.RawEpno);
        Assert.Equal(EpisodeKind.Special, special.Kind);
        Assert.Equal(1, special.ParsedNumber);

        var credit = anime.Episodes.First(e => e.Id == 300);
        Assert.Equal("C1", credit.RawEpno);
        Assert.Equal(EpisodeKind.Credit, credit.Kind);
    }

    [Fact]
    public void Parser_extracts_characters_and_seiyuu()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);

        Assert.Equal(2, anime.Characters.Count);

        var spike = anime.Characters.First(c => c.Name == "Spike Spiegel");
        Assert.Equal("https://cdn.anidb.net/images/main/spike.jpg", spike.ImageUrl);
        Assert.Equal("Yamadera Koichi", spike.SeiyuuName);
        Assert.Equal("https://cdn.anidb.net/images/main/koichi.jpg", spike.SeiyuuImageUrl);

        var faye = anime.Characters.First(c => c.Name == "Faye Valentine");
        Assert.Equal("Hayashibara Megumi", faye.SeiyuuName);
        Assert.Null(faye.ImageUrl);
    }

    [Fact]
    public void Parser_extracts_related_anime()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);

        Assert.Single(anime.RelatedAnime);
        Assert.Equal(2, anime.RelatedAnime[0].Aid);
        Assert.Equal("Sequel", anime.RelatedAnime[0].Type);
    }

    [Fact]
    public void Parser_extracts_tags()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);

        Assert.Contains("space", anime.Tags);
        Assert.Contains("bounty hunter", anime.Tags);
    }

    [Fact]
    public void IsError_detects_error_root_element()
    {
        Assert.True(AniDbAnimeParser.IsError("<error>Banned</error>", out var error));
        Assert.Equal("Banned", error);
    }

    [Fact]
    public void IsError_detects_ban_message()
    {
        Assert.True(AniDbAnimeParser.IsError("<error>Banned - excessive requests</error>", out var error));
        Assert.Contains("Banned", error);
    }

    [Fact]
    public void IsError_returns_false_for_valid_anime()
    {
        Assert.False(AniDbAnimeParser.IsError(SampleAnimeXml, out _));
    }

    [Fact]
    public void IsError_treats_unparseable_response_as_error()
    {
        Assert.True(AniDbAnimeParser.IsError("not xml at all", out var error));
        Assert.Equal("Unparseable response", error);
    }

    // --- AniDbHttpProvider Parse tests ---

    [Fact]
    public void Provider_parse_maps_to_normalized_work_data()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);
        var json = JsonSerializer.Serialize(anime);

        var provider = new AniDbHttpProvider(client: null!);
        var data = provider.Parse(json);

        Assert.Equal(ExternalIdSource.AniDb, data.Source);
        Assert.Equal("Cowboy Bebop", data.CanonicalTitle);
        Assert.Equal("カウボーイビバップ", data.OriginalTitle);
        Assert.Equal(1998, data.ReleaseYear);
        Assert.Equal(26, data.EpisodeCount);
        Assert.Contains("space", data.Genres);
        Assert.Contains("bounty hunter", data.Genres);
    }

    [Fact]
    public void Provider_parse_emits_title_translations()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);
        var json = JsonSerializer.Serialize(anime);

        var data = new AniDbHttpProvider(client: null!).Parse(json);

        Assert.Equal("Cowboy Bebop", data.TitleTranslations["x-jat"]);
        Assert.Equal("Cowboy Bebop", data.TitleTranslations["en"]);
        Assert.Equal("カウボーイビバップ", data.TitleTranslations["ja"]);
        // The de official title wins over the de synonym even though the synonym comes first.
        Assert.Equal("Cowboy Bebop DE", data.TitleTranslations["de"]);
    }

    [Fact]
    public void Provider_parse_derives_finished_status_from_past_end_date()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);
        var json = JsonSerializer.Serialize(anime);

        var data = new AniDbHttpProvider(client: null!).Parse(json);

        Assert.Equal(WorkStatus.Finished, data.Status);
    }

    [Fact]
    public void Provider_parse_derives_ongoing_status_when_started_without_end_date()
    {
        const string xml = """
            <anime id="99">
              <titles><title xml:lang="x-jat" type="main">Endless Show</title></titles>
              <startdate>2000-01-01</startdate>
            </anime>
            """;
        var json = JsonSerializer.Serialize(AniDbAnimeParser.Parse(xml));

        var data = new AniDbHttpProvider(client: null!).Parse(json);

        Assert.Equal(WorkStatus.Ongoing, data.Status);
    }

    [Fact]
    public void Provider_parse_leaves_status_unset_for_partial_dates()
    {
        const string xml = """
            <anime id="99">
              <titles><title xml:lang="x-jat" type="main">Vague Show</title></titles>
              <startdate>1998</startdate>
            </anime>
            """;
        var json = JsonSerializer.Serialize(AniDbAnimeParser.Parse(xml));

        var data = new AniDbHttpProvider(client: null!).Parse(json);

        Assert.Null(data.Status);
    }

    [Fact]
    public void Provider_parse_emits_poster_image()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);
        var json = JsonSerializer.Serialize(anime);

        var data = new AniDbHttpProvider(client: null!).Parse(json);

        Assert.Contains(data.Images, i =>
            i.Type == ImageType.Poster &&
            i.Url == "https://cdn.anidb.net/images/main/12345.jpg" &&
            i.Source == "anidb");
    }

    [Fact]
    public void Provider_parse_emits_voice_actor_credits()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);
        var json = JsonSerializer.Serialize(anime);

        var data = new AniDbHttpProvider(client: null!).Parse(json);

        Assert.Contains(data.Credits, c =>
            c.Name == "Yamadera Koichi" &&
            c.Role == CreditRole.VoiceActor &&
            c.Character == "Spike Spiegel");

        Assert.Contains(data.Credits, c =>
            c.Name == "Hayashibara Megumi" &&
            c.Role == CreditRole.VoiceActor &&
            c.Character == "Faye Valentine");
    }

    [Fact]
    public void Provider_parse_survives_invalid_json()
    {
        var data = new AniDbHttpProvider(client: null!).Parse("not json");

        Assert.Equal(ExternalIdSource.AniDb, data.Source);
        Assert.Null(data.CanonicalTitle);
    }

    [Fact]
    public void Provider_get_external_id_resolves_anidb_source()
    {
        var work = new Work();
        work.ExternalIds.Add(new ExternalId { Source = ExternalIdSource.AniDb, ExternalValue = "1" });

        Assert.Equal("1", new AniDbHttpProvider(client: null!).GetExternalId(work));
    }

    [Fact]
    public void Provider_get_external_id_returns_null_when_absent()
    {
        var work = new Work();
        Assert.Null(new AniDbHttpProvider(client: null!).GetExternalId(work));
    }

    // --- JSON round-trip ---

    [Fact]
    public void AniDbAnime_json_round_trips_correctly()
    {
        var anime = AniDbAnimeParser.Parse(SampleAnimeXml);
        var json = JsonSerializer.Serialize(anime);
        var restored = JsonSerializer.Deserialize<AniDbAnime>(json)!;

        Assert.Equal(anime.Aid, restored.Aid);
        Assert.Equal(anime.MainTitle, restored.MainTitle);
        Assert.Equal(anime.Episodes.Count, restored.Episodes.Count);
        Assert.Equal(anime.Characters.Count, restored.Characters.Count);
        Assert.Equal(anime.RelatedAnime.Count, restored.RelatedAnime.Count);
        Assert.Equal(anime.Tags.Count, restored.Tags.Count);

        var origEp = anime.Episodes.First(e => e.Id == 100);
        var restoredEp = restored.Episodes.First(e => e.Id == 100);
        Assert.Equal(origEp.RawEpno, restoredEp.RawEpno);
        Assert.Equal(origEp.EpnoType, restoredEp.EpnoType);
        Assert.Equal(origEp.Kind, restoredEp.Kind);
        Assert.Equal(origEp.ParsedNumber, restoredEp.ParsedNumber);
        Assert.Equal(origEp.AirDate, restoredEp.AirDate);
    }

    // --- EpisodeKind mapping ---

    [Theory]
    [InlineData(1, EpisodeKind.Regular)]
    [InlineData(2, EpisodeKind.Special)]
    [InlineData(3, EpisodeKind.Credit)]
    [InlineData(4, EpisodeKind.Trailer)]
    [InlineData(5, EpisodeKind.Parody)]
    [InlineData(0, EpisodeKind.Other)]
    [InlineData(99, EpisodeKind.Other)]
    public void EpnoType_maps_to_correct_episode_kind(int epnoType, EpisodeKind expected)
    {
        var ep = new AniDbEpisode { Id = 1, RawEpno = "1", EpnoType = epnoType };
        Assert.Equal(expected, ep.Kind);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("26", 26)]
    [InlineData("S1", 1)]
    [InlineData("S12", 12)]
    [InlineData("C3", 3)]
    [InlineData("T2", 2)]
    [InlineData("P1", 1)]
    [InlineData("O5", 5)]
    [InlineData("notanumber", 0)]
    public void RawEpno_parsed_number_strips_prefix(string rawEpno, int expected)
    {
        var ep = new AniDbEpisode { Id = 1, RawEpno = rawEpno, EpnoType = 1 };
        Assert.Equal(expected, ep.ParsedNumber);
    }
}
