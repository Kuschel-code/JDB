using MetaHub.Domain.Entities;

namespace MetaHub.Identification.AniDb;

/// <summary>Parsed representation of an AniDB HTTP anime XML response.</summary>
public class AniDbAnime
{
    public int Aid { get; init; }

    public string? MainTitle { get; init; }
    public string? OriginalTitle { get; init; }

    public List<AniDbTitle> Titles { get; init; } = new();
    public List<AniDbEpisode> Episodes { get; init; } = new();
    public List<AniDbCharacter> Characters { get; init; } = new();
    public List<AniDbRelatedAnime> RelatedAnime { get; init; } = new();
    public List<string> Tags { get; init; } = new();

    public double? Rating { get; init; }
    public int? EpisodeCount { get; init; }
    public string? Type { get; init; }
    public string? StartDate { get; init; }
    public string? Description { get; init; }
    public string? PictureUrl { get; init; }
}

public class AniDbTitle
{
    public required string Text { get; init; }
    public string? Lang { get; init; }
    public string? Type { get; init; }
}

public class AniDbEpisode
{
    public int Id { get; init; }
    public required string RawEpno { get; init; }
    public int EpnoType { get; init; }
    public string? Title { get; init; }
    public string? TitleEn { get; init; }
    public DateOnly? AirDate { get; init; }
    public int? Length { get; init; }

    public EpisodeKind Kind => EpnoType switch
    {
        1 => EpisodeKind.Regular,
        2 => EpisodeKind.Special,
        3 => EpisodeKind.Credit,
        4 => EpisodeKind.Trailer,
        5 => EpisodeKind.Parody,
        _ => EpisodeKind.Other
    };

    public int ParsedNumber
    {
        get
        {
            var s = RawEpno.TrimStart('S', 'C', 'T', 'P', 'O');
            return int.TryParse(s, out var n) ? n : 0;
        }
    }
}

public class AniDbCharacter
{
    public required string Name { get; init; }
    public string? ImageUrl { get; init; }
    public string? SeiyuuName { get; init; }
    public string? SeiyuuImageUrl { get; init; }
}

public class AniDbRelatedAnime
{
    public int Aid { get; init; }
    public required string Type { get; init; }
    public string? Title { get; init; }
}
