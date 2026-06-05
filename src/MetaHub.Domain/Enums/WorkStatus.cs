namespace MetaHub.Domain.Enums;

/// <summary>
/// Lifecycle status of a work (e.g. an ongoing vs. finished series).
/// </summary>
public enum WorkStatus
{
    Unknown = 0,
    Announced = 1,
    Ongoing = 2,
    Finished = 3,
    Cancelled = 4,
    OnHiatus = 5
}
