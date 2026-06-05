namespace MetaHub.Domain.Entities;

/// <summary>
/// Series/anime specific details. Keyed one-to-one on the owning <see cref="Work"/>.
/// </summary>
public class SeriesDetail
{
    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    public int? SeasonCount { get; set; }
    public int? EpisodeCount { get; set; }
    public string? Network { get; set; }

    public ICollection<Episode> Episodes { get; set; } = new List<Episode>();
}
