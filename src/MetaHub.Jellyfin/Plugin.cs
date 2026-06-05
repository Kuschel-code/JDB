using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MetaHub.Jellyfin.Configuration;

namespace MetaHub.Jellyfin;

/// <summary>
/// MetaHub Jellyfin plugin entry point. Surfaces a tabbed settings page in the admin
/// dashboard and (in later milestones) wires up metadata/image providers that fetch from
/// the MetaHub API.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "MetaHub";

    public override string Description =>
        "Aggregated, cross-linked metadata and artwork served from a self-hosted MetaHub instance.";

    public override Guid Id => Guid.Parse("a3f1c2d4-5b6e-47a8-9c0d-1e2f3a4b5c6d");

    /// <summary>Registers the embedded settings page with the admin dashboard.</summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        };
    }
}
