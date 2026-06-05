namespace MetaHub.Domain.Entities;

/// <summary>
/// A normalized genre/tag shared across works (many-to-many via <see cref="WorkGenre"/>).
/// </summary>
public class Genre
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public ICollection<WorkGenre> WorkGenres { get; set; } = new List<WorkGenre>();
}
