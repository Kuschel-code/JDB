namespace MetaHub.Domain.Entities;

/// <summary>
/// Join entity for the many-to-many relationship between <see cref="Work"/> and <see cref="Genre"/>.
/// </summary>
public class WorkGenre
{
    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    public Guid GenreId { get; set; }
    public Genre? Genre { get; set; }
}
