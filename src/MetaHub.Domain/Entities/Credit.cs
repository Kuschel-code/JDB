using MetaHub.Domain.Enums;

namespace MetaHub.Domain.Entities;

/// <summary>
/// Associates a <see cref="Person"/> with a <see cref="Work"/> in a given role.
/// </summary>
public class Credit
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkId { get; set; }
    public Work? Work { get; set; }

    public Guid PersonId { get; set; }
    public Person? Person { get; set; }

    public CreditRole Role { get; set; }

    /// <summary>The character portrayed, for acting/voice roles.</summary>
    public string? Character { get; set; }

    /// <summary>Display ordering within the role (lower comes first).</summary>
    public int Order { get; set; }
}
