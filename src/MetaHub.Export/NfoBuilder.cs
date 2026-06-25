using System.Xml.Linq;
using MetaHub.Domain.Entities;
using MetaHub.Domain.Enums;

namespace MetaHub.Export;

/// <summary>
/// Builds Jellyfin/Kodi-compatible NFO XML for a <see cref="Work"/>. This is the M5
/// "no-plugin" path: write *.nfo next to media files and let Jellyfin's NFO reader pick
/// them up. Cross-IDs are emitted as &lt;uniqueid&gt; elements.
/// </summary>
public static class NfoBuilder
{
    /// <summary>The NFO file name appropriate for the work's media type.</summary>
    public static string FileNameFor(Work work) => work.MediaType switch
    {
        MediaType.Movie => "movie.nfo",
        MediaType.Series or MediaType.Anime => "tvshow.nfo",
        MediaType.Music => "album.nfo",
        _ => "metahub.nfo"
    };

    public static string Build(Work work) => work.MediaType switch
    {
        MediaType.Movie => BuildMovie(work),
        MediaType.Series or MediaType.Anime => BuildTvShow(work),
        MediaType.Music => BuildAlbum(work),
        MediaType.Book => BuildGeneric(work, "book"),
        _ => BuildGeneric(work, "metahub")
    };

    public static string BuildMovie(Work work) => Document(Root("movie", work));

    public static string BuildTvShow(Work work)
    {
        var root = Root("tvshow", work);
        if (work.SeriesDetail is { } sd)
        {
            if (sd.EpisodeCount is { } ec) root.Add(new XElement("episode", ec));
            if (sd.SeasonCount is { } sc) root.Add(new XElement("season", sc));
            if (!string.IsNullOrWhiteSpace(sd.Network)) root.Add(new XElement("studio", sd.Network));
        }
        return Document(root);
    }

    public static string BuildAlbum(Work work)
    {
        var root = new XElement("album",
            new XElement("title", work.CanonicalTitle));
        AddCommon(root, work);
        if (work.MusicDetail is { } md)
        {
            if (!string.IsNullOrWhiteSpace(md.Label)) root.Add(new XElement("label", md.Label));
            if (md.TrackCount is { } tc) root.Add(new XElement("trackcount", tc));
        }
        return Document(root);
    }

    public static string BuildEpisode(Episode episode, Work series)
    {
        var root = new XElement("episodedetails",
            new XElement("title", episode.Title ?? $"Episode {episode.EpisodeNumber}"),
            new XElement("season", episode.SeasonNumber),
            new XElement("episode", episode.EpisodeNumber));
        if (episode.AbsoluteNumber is { } abs) root.Add(new XElement("absolute_number", abs));
        if (!string.IsNullOrWhiteSpace(episode.Overview)) root.Add(new XElement("plot", episode.Overview));
        if (episode.AirDate is { } air) root.Add(new XElement("aired", air.ToString("yyyy-MM-dd")));
        return Document(root);
    }

    private static XElement Root(string rootName, Work work)
    {
        var root = new XElement(rootName,
            new XElement("title", work.CanonicalTitle));
        if (!string.IsNullOrWhiteSpace(work.OriginalTitle))
            root.Add(new XElement("originaltitle", work.OriginalTitle));
        AddCommon(root, work);
        return root;
    }

    private static void AddCommon(XElement root, Work work)
    {
        if (!string.IsNullOrWhiteSpace(work.Overview))
            root.Add(new XElement("plot", work.Overview));
        if (work.ReleaseYear is { } year)
        {
            root.Add(new XElement("year", year));
            root.Add(new XElement("premiered", $"{year}-01-01"));
        }

        foreach (var genre in work.WorkGenres.Select(wg => wg.Genre?.Name).Where(n => !string.IsNullOrWhiteSpace(n)))
            root.Add(new XElement("genre", genre));

        // Cast & crew. Kodi/Jellyfin NFO conventions: <actor> blocks, <director>, <credits>.
        foreach (var credit in work.Credits.Where(c => c.Person is not null).OrderBy(c => c.Order))
        {
            switch (credit.Role)
            {
                case CreditRole.Actor or CreditRole.VoiceActor:
                    var actor = new XElement("actor", new XElement("name", credit.Person!.Name));
                    if (!string.IsNullOrWhiteSpace(credit.Character))
                        actor.Add(new XElement("role", credit.Character));
                    if (!string.IsNullOrWhiteSpace(credit.Person.ImageUrl))
                        actor.Add(new XElement("thumb", credit.Person.ImageUrl));
                    root.Add(actor);
                    break;
                case CreditRole.Director:
                    root.Add(new XElement("director", credit.Person!.Name));
                    break;
                case CreditRole.Writer or CreditRole.Author:
                    root.Add(new XElement("credits", credit.Person!.Name));
                    break;
            }
        }

        foreach (var poster in work.Images.Where(i => i.Type == ImageType.Poster).OrderByDescending(i => i.Score))
            root.Add(new XElement("thumb", new XAttribute("aspect", "poster"), poster.Url));
        foreach (var backdrop in work.Images.Where(i => i.Type == ImageType.Backdrop).OrderByDescending(i => i.Score))
            root.Add(new XElement("fanart", new XElement("thumb", backdrop.Url)));

        // Cross-IDs as <uniqueid type="..."> with a default flag for the primary one.
        var defaultSource = PrimarySource(work.MediaType);
        foreach (var ext in work.ExternalIds)
        {
            var type = JellyfinType(ext.Source);
            if (type is null) continue;
            root.Add(new XElement("uniqueid",
                new XAttribute("type", type),
                new XAttribute("default", ext.Source == defaultSource),
                ext.ExternalValue));
        }
    }

    private static string BuildGeneric(Work work, string rootName) => Document(Root(rootName, work));

    private static string Document(XElement root)
        => new XDeclaration("1.0", "utf-8", "yes") + Environment.NewLine + root;

    private static ExternalIdSource PrimarySource(MediaType type) => type switch
    {
        MediaType.Movie or MediaType.Series => ExternalIdSource.Tmdb,
        MediaType.Anime => ExternalIdSource.AniDb,
        MediaType.Music => ExternalIdSource.MusicBrainz,
        MediaType.Book => ExternalIdSource.Isbn,
        _ => ExternalIdSource.Unknown
    };

    private static string? JellyfinType(ExternalIdSource source) => source switch
    {
        ExternalIdSource.Tmdb => "tmdb",
        ExternalIdSource.TmdbMovie => "tmdb", // movie ids export under the same NFO uniqueid type
        ExternalIdSource.Imdb => "imdb",
        ExternalIdSource.Tvdb => "tvdb",
        ExternalIdSource.AniDb => "anidb",
        ExternalIdSource.AniList => "anilist",
        ExternalIdSource.Mal => "mal",
        ExternalIdSource.Kitsu => "kitsu",
        ExternalIdSource.MusicBrainz => "musicbrainz",
        ExternalIdSource.Isbn => "isbn",
        _ => null
    };
}
