namespace MetaHub.Domain.Entities;

/// <summary>
/// Book specific details. Keyed one-to-one on the owning <see cref="Work"/>.
/// </summary>
public class BookDetail
{
    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    public string? Isbn13 { get; set; }
    public int? PageCount { get; set; }
    public string? Publisher { get; set; }

    public string? SeriesName { get; set; }
    public double? SeriesIndex { get; set; }
}
