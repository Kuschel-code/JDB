namespace MetaHub.Domain.Entities;

/// <summary>
/// A person (or organisation, e.g. a studio/label) that can be credited on works.
/// </summary>
public class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? SortName { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>Per-source biographies, e.g. {"tmdb": "...", "anilist": "..."}. Stored as JSONB.</summary>
    public Dictionary<string, string> Bios { get; set; } = new();

    public ICollection<Credit> Credits { get; set; } = new List<Credit>();
}
