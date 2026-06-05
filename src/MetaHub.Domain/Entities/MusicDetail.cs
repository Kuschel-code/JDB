namespace MetaHub.Domain.Entities;

/// <summary>
/// Music (album/release) specific details. Keyed one-to-one on the owning <see cref="Work"/>.
/// </summary>
public class MusicDetail
{
    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    /// <summary>e.g. Album, EP, Single, Compilation.</summary>
    public string? AlbumType { get; set; }
    public string? Label { get; set; }
    public int? TrackCount { get; set; }

    public ICollection<Track> Tracks { get; set; } = new List<Track>();
}
