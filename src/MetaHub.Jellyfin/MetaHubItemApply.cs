using System.Linq;
using MediaBrowser.Model.Entities;
using MetaHub.Jellyfin.Api;

namespace MetaHub.Jellyfin;

/// <summary>
/// Pure decision logic for the "apply best metadata to the library" task: given an item's current
/// fields and MetaHub's resolved work, decide which fields to overwrite. A field changes only when
/// MetaHub has a (non-blank) value, it differs from the current one, and the field is not locked.
/// <see cref="WorkDto.CanonicalTitle"/> is already localized to the viewer's language by the
/// backend, so the task compares against it directly.
/// </summary>
public static class MetaHubItemApply
{
    /// <summary>An item's current values the task may replace.</summary>
    public sealed record ItemFields(string? Name, string? Overview, int? ProductionYear);

    /// <summary>The fields to write back (null = leave unchanged).</summary>
    public sealed class ItemFieldChanges
    {
        public string? Name { get; set; }
        public string? Overview { get; set; }
        public int? ProductionYear { get; set; }

        public bool HasAny => Name is not null || Overview is not null || ProductionYear is not null;
    }

    public static ItemFieldChanges Compute(
        ItemFields current, WorkDto work, IReadOnlyCollection<MetadataField> lockedFields)
    {
        var changes = new ItemFieldChanges();

        if (!lockedFields.Contains(MetadataField.Name) && Differs(work.CanonicalTitle, current.Name))
            changes.Name = work.CanonicalTitle;

        if (!lockedFields.Contains(MetadataField.Overview) && Differs(work.Overview, current.Overview))
            changes.Overview = work.Overview;

        if (work.ReleaseYear is { } year && year != current.ProductionYear)
            changes.ProductionYear = year;

        return changes;
    }

    /// <summary>MetaHub has a usable value that differs from the item's current one.</summary>
    private static bool Differs(string? metaHubValue, string? current)
        => !string.IsNullOrWhiteSpace(metaHubValue) && metaHubValue != current;
}
