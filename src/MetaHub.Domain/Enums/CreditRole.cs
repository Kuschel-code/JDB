namespace MetaHub.Domain.Enums;

/// <summary>
/// The role a <see cref="Entities.Person"/> plays in a work.
/// </summary>
public enum CreditRole
{
    Unknown = 0,
    Actor = 1,
    Director = 2,
    Writer = 3,
    Producer = 4,
    Composer = 5,
    Author = 6,
    Artist = 7,
    VoiceActor = 8,
    Studio = 9
}
