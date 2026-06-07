using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MetaHub.Jellyfin.Api;
using MetaHub.Jellyfin.Configuration;

namespace MetaHub.Jellyfin;

/// <summary>
/// Decides whether MetaHub may surface metadata/artwork for a given item. An item is blocked
/// when its media type is disabled, or when the item itself or any ancestor is in
/// <see cref="PluginConfiguration.DisabledItemIds"/>. Fails open: if an item cannot be resolved
/// it is served (a lookup miss never hides data).
/// </summary>
public sealed class MetaHubItemGate
{
    private readonly ILibraryManager _libraryManager;

    public MetaHubItemGate(ILibraryManager libraryManager) => _libraryManager = libraryManager;

    /// <summary>Pure core: is any of the item's own/ancestor ids in the disabled set?</summary>
    public static bool IsBlocked(IEnumerable<Guid> selfAndAncestorIds, IReadOnlyCollection<string> disabledItemIds)
    {
        if (disabledItemIds.Count == 0)
            return false;

        var disabled = new HashSet<string>(disabledItemIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in selfAndAncestorIds)
        {
            if (id != Guid.Empty && disabled.Contains(id.ToString("N")))
                return true;
        }

        return false;
    }

    /// <summary>Image provider path: the item is already in hand.</summary>
    public bool IsServed(BaseItem item, string mediaType, PluginConfiguration config)
    {
        if (!MetaHubMapping.IsMediaTypeEnabled(mediaType, config))
            return false;
        if (config.DisabledItemIds.Length == 0)
            return true;
        return !IsBlocked(SelfAndAncestors(item), config.DisabledItemIds);
    }

    /// <summary>Metadata provider path: resolve the item from its provider ids first.</summary>
    public bool IsServed(ItemLookupInfo info, string mediaType, PluginConfiguration config)
    {
        if (!MetaHubMapping.IsMediaTypeEnabled(mediaType, config))
            return false;
        if (config.DisabledItemIds.Length == 0)
            return true;

        var item = ResolveItem(info.ProviderIds);
        if (item is null)
            return true; // fail open — nothing to gate yet
        return !IsBlocked(SelfAndAncestors(item), config.DisabledItemIds);
    }

    private static IEnumerable<Guid> SelfAndAncestors(BaseItem item)
    {
        yield return item.Id;
        foreach (var parent in item.GetParents())
            yield return parent.Id;
    }

    private BaseItem? ResolveItem(IReadOnlyDictionary<string, string>? providerIds)
    {
        if (providerIds is null || providerIds.Count == 0)
            return null;

        var query = new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string>(providerIds)
        };
        return _libraryManager.GetItemList(query).FirstOrDefault();
    }
}
