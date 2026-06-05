namespace MetaHub.Domain.Entities;

/// <summary>
/// A single track on an album (the music counterpart to an <see cref="Episode"/>).
/// </summary>
public class Track
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning album work (FK to <see cref="MusicDetail.WorkId"/>).</summary>
    public Guid AlbumWorkId { get; set; }
    public MusicDetail? Album { get; set; }

    public int DiscNumber { get; set; } = 1;
    public int TrackNumber { get; set; }
    public int? LengthMs { get; set; }
    public string Title { get; set; } = string.Empty;
}
