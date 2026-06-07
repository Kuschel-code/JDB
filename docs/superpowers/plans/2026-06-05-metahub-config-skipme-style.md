# MetaHub SkipMe.db-style config page — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace MetaHub's plugin config page with a SkipMe.db-style per-item manager (library → series → season + movies) with instant-saving toggles, and gate the metadata/image providers so disabled items are not surfaced to Jellyfin.

**Architecture:** An opt-out disabled-set (`DisabledItemIds`) on `PluginConfiguration`; a `MetaHubItemGate` whose pure core decides "blocked?" from an item's id + ancestor ids vs. the set (the providers resolve the item — via `ILibraryManager` for `*Info`, or directly for the image `BaseItem`); a rewritten `configPage.html` that lists libraries/items via the Jellyfin web `ApiClient` and saves toggles with a debounced `updatePluginConfiguration`.

**Tech Stack:** .NET 9, Jellyfin 10.11 plugin SDK (`Jellyfin.Controller`), xUnit, vanilla JS + Jellyfin `ApiClient` / `Dashboard` globals.

**Working directory:** `C:\Users\Kuscheltier\JDB`, branch `claude/config-skipme-style`.
Spec: `docs/superpowers/specs/2026-06-05-metahub-config-skipme-style-design.md`.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `src/MetaHub.Jellyfin/Configuration/PluginConfiguration.cs` | + `DisabledItemIds` field (the persisted opt-out set) |
| `src/MetaHub.Jellyfin/MetaHubItemGate.cs` | **new** — decides whether MetaHub may serve an item (pure `IsBlocked` + `ILibraryManager`-backed resolution) |
| `src/MetaHub.Jellyfin/Providers/MetaHubSeriesProvider.cs` | inject + call the gate |
| `src/MetaHub.Jellyfin/Providers/MetaHubMovieProvider.cs` | inject + call the gate |
| `src/MetaHub.Jellyfin/Providers/MetaHubImageProvider.cs` | inject + call the gate (BaseItem overload) |
| `src/MetaHub.Jellyfin/MetaHubServiceRegistrator.cs` | register `MetaHubItemGate` |
| `src/MetaHub.Jellyfin/Configuration/configPage.html` | full UI rewrite |
| `tests/MetaHub.Tests/MetaHub.Tests.csproj` | + ProjectReference to `MetaHub.Jellyfin` |
| `tests/MetaHub.Tests/MetaHubItemGateTests.cs` | **new** — unit tests for the gate's pure core |

---

## Task 1: Add the `DisabledItemIds` setting

**Files:**
- Modify: `src/MetaHub.Jellyfin/Configuration/PluginConfiguration.cs`

- [ ] **Step 1: Add the field**

In `PluginConfiguration`, immediately after the `EnableBooks` property (in the
`// --- Library: which content + language (both modes) ---` section), add:

```csharp
    /// <summary>
    /// Jellyfin item GUIDs ("N" format) of libraries, series, seasons or movies for which
    /// MetaHub must NOT surface metadata/artwork. Empty = everything enabled (opt-out model).
    /// Children inherit a disabled ancestor, so only the highest disabled node is stored.
    /// </summary>
    public string[] DisabledItemIds { get; set; } = System.Array.Empty<string>();
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/MetaHub.Jellyfin/MetaHub.Jellyfin.csproj -c Release --nologo`
Expected: `0 Fehler`.

- [ ] **Step 3: Commit**

```bash
git add src/MetaHub.Jellyfin/Configuration/PluginConfiguration.cs
git commit -m "Add DisabledItemIds opt-out set to plugin configuration"
```

---

## Task 2: `MetaHubItemGate` (pure core, TDD)

**Files:**
- Modify: `tests/MetaHub.Tests/MetaHub.Tests.csproj`
- Create: `tests/MetaHub.Tests/MetaHubItemGateTests.cs`
- Create: `src/MetaHub.Jellyfin/MetaHubItemGate.cs`

- [ ] **Step 1: Reference MetaHub.Jellyfin from the test project**

In `tests/MetaHub.Tests/MetaHub.Tests.csproj`, inside the existing `<ItemGroup>` that holds the
`ProjectReference` elements, add:

```xml
    <ProjectReference Include="..\..\src\MetaHub.Jellyfin\MetaHub.Jellyfin.csproj" />
```

- [ ] **Step 2: Write the failing tests**

Create `tests/MetaHub.Tests/MetaHubItemGateTests.cs`:

```csharp
using MetaHub.Jellyfin;
using Xunit;

namespace MetaHub.Tests;

/// <summary>
/// Covers the pure decision core of the gate (id + ancestor ids vs. the disabled set).
/// The ILibraryManager-backed resolution is a thin wrapper and is exercised live, not here.
/// </summary>
public class MetaHubItemGateTests
{
    [Fact]
    public void Empty_disabled_set_serves_everything()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Assert.False(MetaHubItemGate.IsBlocked(ids, Array.Empty<string>()));
    }

    [Fact]
    public void Blocks_when_item_itself_is_disabled()
    {
        var item = Guid.NewGuid();
        Assert.True(MetaHubItemGate.IsBlocked(new[] { item }, new[] { item.ToString("N") }));
    }

    [Fact]
    public void Blocks_when_an_ancestor_is_disabled()
    {
        var item = Guid.NewGuid();
        var series = Guid.NewGuid();
        var library = Guid.NewGuid();
        Assert.True(MetaHubItemGate.IsBlocked(
            new[] { item, series, library }, new[] { library.ToString("N") }));
    }

    [Fact]
    public void Serves_when_neither_item_nor_ancestor_is_disabled()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        Assert.False(MetaHubItemGate.IsBlocked(ids, new[] { Guid.NewGuid().ToString("N") }));
    }

    [Fact]
    public void Ignores_empty_guids_and_is_case_insensitive()
    {
        var item = Guid.NewGuid();
        Assert.False(MetaHubItemGate.IsBlocked(new[] { Guid.Empty }, new[] { Guid.Empty.ToString("N") }));
        Assert.True(MetaHubItemGate.IsBlocked(new[] { item }, new[] { item.ToString("N").ToUpperInvariant() }));
    }
}
```

- [ ] **Step 3: Create the gate so the tests compile/run**

Create `src/MetaHub.Jellyfin/MetaHubItemGate.cs`:

```csharp
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
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test MetaHub.sln -c Release --nologo --filter "FullyQualifiedName~MetaHubItemGateTests"`
Expected: `erfolgreich: 5, Fehler: 0`. (The first build may pull Jellyfin assemblies; that is expected.)

- [ ] **Step 5: Commit**

```bash
git add tests/MetaHub.Tests/MetaHub.Tests.csproj tests/MetaHub.Tests/MetaHubItemGateTests.cs src/MetaHub.Jellyfin/MetaHubItemGate.cs
git commit -m "Add MetaHubItemGate with per-item ancestry gating + tests"
```

---

## Task 3: Register the gate and wire it into the providers

**Files:**
- Modify: `src/MetaHub.Jellyfin/MetaHubServiceRegistrator.cs`
- Modify: `src/MetaHub.Jellyfin/Providers/MetaHubSeriesProvider.cs`
- Modify: `src/MetaHub.Jellyfin/Providers/MetaHubMovieProvider.cs`
- Modify: `src/MetaHub.Jellyfin/Providers/MetaHubImageProvider.cs`

- [ ] **Step 1: Register the gate in DI**

In `MetaHubServiceRegistrator.RegisterServices`, immediately before the line
`services.AddSingleton<IMetaHubBackend, MetaHubBackend>();`, add:

```csharp
        // Per-item enable/disable gate (resolves library ancestry; ILibraryManager is host-provided).
        services.AddSingleton<MetaHubItemGate>();
```

- [ ] **Step 2: Inject + call the gate in the series provider**

In `src/MetaHub.Jellyfin/Providers/MetaHubSeriesProvider.cs` add `using MetaHub.Jellyfin;` and
replace the fields + constructor with:

```csharp
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;

    public MetaHubSeriesProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory, MetaHubItemGate gate)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
    }
```

Replace the guard:

```csharp
        if (work is null || !MetaHubMapping.IsMediaTypeEnabled(work.MediaType, config))
            return result;
```

with:

```csharp
        if (work is null || !_gate.IsServed(info, work.MediaType, config))
            return result;
```

- [ ] **Step 3: Inject + call the gate in the movie provider**

In `src/MetaHub.Jellyfin/Providers/MetaHubMovieProvider.cs` add `using MetaHub.Jellyfin;` and
replace the fields + constructor with:

```csharp
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;

    public MetaHubMovieProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory, MetaHubItemGate gate)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
    }
```

Replace the guard:

```csharp
        if (work is null || !MetaHubMapping.IsMediaTypeEnabled(work.MediaType, config))
            return result;
```

with:

```csharp
        if (work is null || !_gate.IsServed(info, work.MediaType, config))
            return result;
```

- [ ] **Step 4: Inject + call the gate in the image provider**

In `src/MetaHub.Jellyfin/Providers/MetaHubImageProvider.cs` add `using MetaHub.Jellyfin;` and
replace the fields + constructor with:

```csharp
    private readonly IMetaHubBackend _backend;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MetaHubItemGate _gate;

    public MetaHubImageProvider(IMetaHubBackend backend, IHttpClientFactory httpClientFactory, MetaHubItemGate gate)
    {
        _backend = backend;
        _httpClientFactory = httpClientFactory;
        _gate = gate;
    }
```

Replace the guard (the `BaseItem item` is already a parameter of `GetImages`):

```csharp
        if (work is null || !MetaHubMapping.IsMediaTypeEnabled(work.MediaType, config))
            return Enumerable.Empty<RemoteImageInfo>();
```

with:

```csharp
        if (work is null || !_gate.IsServed(item, work.MediaType, config))
            return Enumerable.Empty<RemoteImageInfo>();
```

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build MetaHub.sln -c Release --nologo`
Expected: `0 Warnung(en)`, `0 Fehler`.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test MetaHub.sln -c Release --no-build --nologo`
Expected: all tests pass except the two pre-existing Windows-only temp-file-delete flakes
(`EnsureCreated_then_roundtrip_work_with_translations`, `EmbeddedSqlitePipelineTests.Ingest_then_merge_runs_on_sqlite`)
which fail with `System.IO.IOException` at `FileSystem.DeleteFile` — environmental, not regressions.
`MetaHubItemGateTests` pass.

- [ ] **Step 7: Commit**

```bash
git add src/MetaHub.Jellyfin/MetaHubServiceRegistrator.cs src/MetaHub.Jellyfin/Providers/MetaHubSeriesProvider.cs src/MetaHub.Jellyfin/Providers/MetaHubMovieProvider.cs src/MetaHub.Jellyfin/Providers/MetaHubImageProvider.cs
git commit -m "Gate metadata/image providers on the per-item disabled set"
```

---

## Task 4: Rewrite `configPage.html` (SkipMe.db-style)

**Files:**
- Modify (full replace): `src/MetaHub.Jellyfin/Configuration/configPage.html`

- [ ] **Step 1: Replace the file with the new UI**

Write `src/MetaHub.Jellyfin/Configuration/configPage.html` with exactly this content:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>MetaHub</title>
    <style>
        #metaHubPage .mhHeader { margin: 0 0 1.5em; }
        #metaHubPage .mhHeader h2 { margin: 0 0 .3em; }
        #metaHubPage .mhSub { opacity: .7; max-width: 60em; }
        #metaHubPage .mhPills { display: flex; gap: .5em; margin: 1em 0 1.5em; }
        #metaHubPage .mhPill { padding: .4em 1em; border-radius: 999px; background: #222; cursor: pointer; opacity: .7; }
        #metaHubPage .mhPill.mhActive { background: #444; opacity: 1; }
        #metaHubPage .mhFilter { width: 100%; padding: .8em 1em; margin-bottom: 1.5em; background: #2a2a2a; border: 0; border-radius: 6px; color: #fff; }
        #metaHubPage .mhRow { display: flex; align-items: center; gap: 1em; padding: .7em 1em; margin-bottom: .6em; background: #1c1c1c; border: 1px solid #2a2a2a; border-radius: 10px; }
        #metaHubPage .mhRow.mhChild { margin-left: 2.5em; }
        #metaHubPage .mhThumb { width: 42px; height: 60px; flex: 0 0 auto; border-radius: 4px; background: #333 center/cover no-repeat; }
        #metaHubPage .mhMeta { flex: 1 1 auto; min-width: 0; }
        #metaHubPage .mhName { font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        #metaHubPage .mhHint { opacity: .55; font-size: .85em; }
        #metaHubPage .mhChevron { cursor: pointer; padding: .2em .6em; opacity: .7; user-select: none; }
        #metaHubPage .mhSwitch { position: relative; width: 46px; height: 24px; flex: 0 0 auto; }
        #metaHubPage .mhSwitch input { opacity: 0; width: 0; height: 0; }
        #metaHubPage .mhSlider { position: absolute; inset: 0; background: #555; border-radius: 999px; transition: .2s; cursor: pointer; }
        #metaHubPage .mhSlider:before { content: ""; position: absolute; height: 18px; width: 18px; left: 3px; top: 3px; background: #fff; border-radius: 50%; transition: .2s; }
        #metaHubPage .mhSwitch input:checked + .mhSlider { background: #00a4dc; }
        #metaHubPage .mhSwitch input:checked + .mhSlider:before { transform: translateX(22px); }
        #metaHubPage .mhSwitch input:disabled + .mhSlider { opacity: .4; cursor: not-allowed; }
        #metaHubPage .mhMore { width: 100%; padding: .6em; background: #222; border: 0; border-radius: 8px; color: #fff; cursor: pointer; }
        #metaHubPage .hide { display: none !important; }
        #metaHubPage .mhCard { background: #1c1c1c; border: 1px solid #2a2a2a; border-radius: 10px; padding: 1.2em; margin-bottom: 1em; }
    </style>
</head>
<body>
<div id="metaHubPage" data-role="page" class="page type-interior pluginConfigurationPage">
    <div data-role="content">
        <div class="content-primary">

            <div class="mhHeader">
                <h2>MetaHub &ndash; Settings</h2>
                <div class="mhSub">Toggle MetaHub metadata on or off for individual libraries, series, seasons or movies. Data stays in the local database but is not surfaced to Jellyfin when disabled.</div>
            </div>

            <div class="mhPills">
                <div class="mhPill mhActive" data-pill="libraries">Libraries</div>
                <div class="mhPill" data-pill="engine">Engine</div>
                <div class="mhPill" data-pill="about">About</div>
            </div>

            <!-- Libraries -->
            <div class="mhPane" data-pane="libraries">
                <input id="mhFilter" class="mhFilter" type="text" placeholder="Start typing a name..." />
                <div id="mhTree"></div>
                <div id="mhSearchResults" class="hide"></div>
            </div>

            <!-- Engine -->
            <div class="mhPane hide" data-pane="engine">
                <form id="mhEngineForm">
                    <div class="mhCard">
                        <label class="checkboxContainer">
                            <input id="UseEmbeddedEngine" type="checkbox" is="emby-checkbox" />
                            <span>Run embedded (local SQLite &mdash; no server / no Docker)</span>
                        </label>
                        <div id="mhRemoteBox">
                            <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="ApiBaseUrl">MetaHub API URL</label><input id="ApiBaseUrl" type="url" is="emby-input" /></div>
                            <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="ApiKey">API key</label><input id="ApiKey" type="password" is="emby-input" /></div>
                            <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="RequestTimeoutSeconds">Request timeout (seconds)</label><input id="RequestTimeoutSeconds" type="number" min="1" max="600" is="emby-input" /></div>
                        </div>
                    </div>
                    <div class="mhCard">
                        <h3>Content types</h3>
                        <label class="checkboxContainer"><input id="EnableMovies" type="checkbox" is="emby-checkbox" /><span>Movies</span></label>
                        <label class="checkboxContainer"><input id="EnableSeries" type="checkbox" is="emby-checkbox" /><span>Series</span></label>
                        <label class="checkboxContainer"><input id="EnableAnime" type="checkbox" is="emby-checkbox" /><span>Anime</span></label>
                        <label class="checkboxContainer"><input id="EnableMusic" type="checkbox" is="emby-checkbox" /><span>Music</span></label>
                        <label class="checkboxContainer"><input id="EnableBooks" type="checkbox" is="emby-checkbox" /><span>Books</span></label>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="PreferredLanguage">Preferred language</label><input id="PreferredLanguage" type="text" is="emby-input" placeholder="de" /></div>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="FallbackLanguage">Fallback language</label><input id="FallbackLanguage" type="text" is="emby-input" placeholder="en" /></div>
                    </div>
                    <div class="mhCard">
                        <h3>Enrichment</h3>
                        <div class="selectContainer"><label class="selectLabel" for="WriteMode">Write mode</label>
                            <select is="emby-select" id="WriteMode">
                                <option value="FillMissingOnly">Fill missing only (keep existing)</option>
                                <option value="Overwrite">Overwrite existing</option>
                            </select>
                        </div>
                        <label class="checkboxContainer"><input id="EnrichOnDemand" type="checkbox" is="emby-checkbox" /><span>Enrich on demand (when a work is first matched)</span></label>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="TmdbApiKey">TMDB API key</label><input id="TmdbApiKey" type="password" is="emby-input" /></div>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="GoogleBooksApiKey">Google Books API key</label><input id="GoogleBooksApiKey" type="password" is="emby-input" /></div>
                        <h3>AniDB identification</h3>
                        <label class="checkboxContainer"><input id="AniDbEnabled" type="checkbox" is="emby-checkbox" /><span>Enable AniDB file lookup</span></label>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="AniDbClientName">AniDB client name</label><input id="AniDbClientName" type="text" is="emby-input" /></div>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="AniDbClientVersion">AniDB client version</label><input id="AniDbClientVersion" type="number" min="1" is="emby-input" /></div>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="AniDbUsername">AniDB username</label><input id="AniDbUsername" type="text" is="emby-input" /></div>
                        <div class="inputContainer"><label class="inputLabel inputLabelUnfocused" for="AniDbPassword">AniDB password</label><input id="AniDbPassword" type="password" is="emby-input" /></div>
                    </div>
                    <button is="emby-button" type="submit" class="raised button-submit block"><span>Save</span></button>
                </form>
            </div>

            <!-- About -->
            <div class="mhPane hide" data-pane="about">
                <div class="mhCard">
                    <p>MetaHub aggregates cross-linked metadata and artwork for music, movies, series, anime and books.</p>
                    <p>In embedded mode everything runs inside Jellyfin with a local SQLite database; datasets are fetched from GitHub. Run <b>MetaHub: Update anime mappings</b> then <b>MetaHub: Enrich metadata</b> from Dashboard &rarr; Scheduled Tasks.</p>
                    <p><a is="emby-linkbutton" class="button-link" href="https://github.com/Kuschel-code/JDB" target="_blank">Project on GitHub</a></p>
                </div>
            </div>

        </div>
    </div>

    <script type="text/javascript">
        (function () {
            var pluginId = 'a3f1c2d4-5b6e-47a8-9c0d-1e2f3a4b5c6d';
            var page, config, disabled, saveTimer;

            var checks = ['UseEmbeddedEngine','EnableMovies','EnableSeries','EnableAnime','EnableMusic','EnableBooks','EnrichOnDemand','AniDbEnabled'];
            var texts = ['ApiBaseUrl','ApiKey','RequestTimeoutSeconds','PreferredLanguage','FallbackLanguage','WriteMode','TmdbApiKey','GoogleBooksApiKey','AniDbClientName','AniDbClientVersion','AniDbUsername','AniDbPassword'];

            function norm(id) { return (id || '').replace(/-/g, '').toLowerCase(); }
            function isDisabled(id) { return disabled.indexOf(norm(id)) !== -1; }
            function setDisabled(id, off) {
                var n = norm(id), i = disabled.indexOf(n);
                if (off && i === -1) disabled.push(n);
                if (!off && i !== -1) disabled.splice(i, 1);
                queueSave();
            }
            function queueSave() {
                clearTimeout(saveTimer);
                saveTimer = setTimeout(function () {
                    config.DisabledItemIds = disabled.slice();
                    ApiClient.updatePluginConfiguration(pluginId, config);
                }, 300);
            }

            function showPill(name) {
                page.querySelectorAll('.mhPane').forEach(function (p) { p.classList.toggle('hide', p.getAttribute('data-pane') !== name); });
                page.querySelectorAll('.mhPill').forEach(function (t) { t.classList.toggle('mhActive', t.getAttribute('data-pill') === name); });
            }

            // Build one toggle row. parentOff = an ancestor is disabled (inherited, non-interactive).
            function row(item, opts) {
                opts = opts || {};
                var el = document.createElement('div');
                el.className = 'mhRow' + (opts.child ? ' mhChild' : '');
                var parentOff = !!opts.parentOff;
                var off = parentOff || isDisabled(item.Id);

                var chevron = opts.expandable ? '<div class="mhChevron" data-act="expand">&#9656;</div>' : '';
                var thumb = '';
                if (opts.thumb !== false) {
                    var url = ApiClient.getImageUrl(item.Id, { type: 'Primary', maxHeight: 80 });
                    thumb = '<div class="mhThumb" style="background-image:url(\'' + url + '\')"></div>';
                }
                el.innerHTML =
                    thumb +
                    '<div class="mhMeta"><div class="mhName">' + (item.Name || '') + '</div>' +
                    (opts.hint ? '<div class="mhHint">' + opts.hint + '</div>' : '') + '</div>' +
                    chevron +
                    '<label class="mhSwitch"><input type="checkbox"' + (off ? '' : ' checked') + (parentOff ? ' disabled' : '') + ' /><span class="mhSlider"></span></label>';

                var input = el.querySelector('input');
                input.addEventListener('change', function () { setDisabled(item.Id, !input.checked); });

                var childBox = null;
                if (opts.expandable) {
                    childBox = document.createElement('div');
                    childBox.className = 'hide';
                    el.querySelector('[data-act=expand]').addEventListener('click', function () {
                        var willShow = childBox.classList.contains('hide');
                        childBox.classList.toggle('hide');
                        this.innerHTML = willShow ? '&#9662;' : '&#9656;';
                        if (willShow && !childBox.dataset.loaded) {
                            childBox.dataset.loaded = '1';
                            opts.loadChildren(childBox, off);
                        }
                    });
                }
                return { el: el, childBox: childBox };
            }

            function appendItems(container, items, parentOff) {
                items.forEach(function (it) {
                    var expandable = it.Type === 'Series';
                    var r = row(it, {
                        child: true,
                        parentOff: parentOff,
                        hint: expandable ? 'Expand to manage individual seasons' : '',
                        expandable: expandable,
                        loadChildren: function (box, off) {
                            ApiClient.getItems(ApiClient.getCurrentUserId(), {
                                ParentId: it.Id, IncludeItemTypes: 'Season', Recursive: false, SortBy: 'SortName'
                            }).then(function (res) { appendItems(box, res.Items, off || isDisabled(it.Id)); });
                        }
                    });
                    container.appendChild(r.el);
                    if (r.childBox) container.appendChild(r.childBox);
                });
            }

            function loadLibraryPage(library, box, startIndex, parentOff) {
                ApiClient.getItems(ApiClient.getCurrentUserId(), {
                    ParentId: library.ItemId, IncludeItemTypes: 'Series,Movie', Recursive: false,
                    SortBy: 'SortName', StartIndex: startIndex, Limit: 100, Fields: 'PrimaryImageAspectRatio'
                }).then(function (res) {
                    var holder = document.createElement('div');
                    appendItems(holder, res.Items, parentOff);
                    while (holder.firstChild) box.appendChild(holder.firstChild);
                    var loaded = startIndex + res.Items.length;
                    var oldMore = box.querySelector('.mhMore'); if (oldMore) oldMore.remove();
                    if (loaded < res.TotalRecordCount) {
                        var more = document.createElement('button');
                        more.className = 'mhMore'; more.textContent = 'Load more (' + loaded + '/' + res.TotalRecordCount + ')';
                        more.addEventListener('click', function () { more.remove(); loadLibraryPage(library, box, loaded, parentOff); });
                        box.appendChild(more);
                    }
                });
            }

            function renderTree() {
                var tree = page.querySelector('#mhTree');
                tree.innerHTML = '';
                ApiClient.getVirtualFolders().then(function (libs) {
                    libs.forEach(function (lib) {
                        var libItem = { Id: lib.ItemId, Name: lib.Name };
                        var r = row(libItem, {
                            thumb: false,
                            hint: 'Expand to manage individual items',
                            expandable: true,
                            loadChildren: function (box, off) { loadLibraryPage(lib, box, 0, off || isDisabled(lib.ItemId)); }
                        });
                        tree.appendChild(r.el);
                        if (r.childBox) tree.appendChild(r.childBox);
                    });
                });
            }

            var searchTimer;
            function onFilter(term) {
                var tree = page.querySelector('#mhTree');
                var results = page.querySelector('#mhSearchResults');
                clearTimeout(searchTimer);
                if (!term) { tree.classList.remove('hide'); results.classList.add('hide'); results.innerHTML = ''; return; }
                searchTimer = setTimeout(function () {
                    tree.classList.add('hide'); results.classList.remove('hide');
                    ApiClient.getItems(ApiClient.getCurrentUserId(), {
                        SearchTerm: term, IncludeItemTypes: 'Series,Movie', Recursive: true, Limit: 100,
                        SortBy: 'SortName', Fields: 'PrimaryImageAspectRatio'
                    }).then(function (res) {
                        results.innerHTML = '';
                        appendItems(results, res.Items, false);
                    });
                }, 300);
            }

            function toggleRemote() { page.querySelector('#mhRemoteBox').classList.toggle('hide', page.querySelector('#UseEmbeddedEngine').checked); }

            function loadEngine() {
                checks.forEach(function (k) { var el = page.querySelector('#' + k); if (el) el.checked = !!config[k]; });
                texts.forEach(function (k) { var el = page.querySelector('#' + k); if (el) el.value = config[k] != null ? config[k] : ''; });
                toggleRemote();
            }

            function saveEngine() {
                Dashboard.showLoadingMsg();
                checks.forEach(function (k) { var el = page.querySelector('#' + k); if (el) config[k] = el.checked; });
                texts.forEach(function (k) { var el = page.querySelector('#' + k); if (el) config[k] = el.value; });
                config.RequestTimeoutSeconds = parseInt(config.RequestTimeoutSeconds, 10) || 30;
                config.AniDbClientVersion = parseInt(config.AniDbClientVersion, 10) || 1;
                config.DisabledItemIds = disabled.slice();
                ApiClient.updatePluginConfiguration(pluginId, config).then(Dashboard.processPluginConfigurationUpdateResult);
            }

            document.querySelector('#metaHubPage').addEventListener('pageshow', function () {
                page = this;
                Dashboard.showLoadingMsg();
                page.querySelectorAll('.mhPill').forEach(function (t) { t.addEventListener('click', function () { showPill(t.getAttribute('data-pill')); }); });
                page.querySelector('#mhFilter').addEventListener('input', function () { onFilter(this.value.trim()); });
                page.querySelector('#UseEmbeddedEngine').addEventListener('change', toggleRemote);
                page.querySelector('#mhEngineForm').addEventListener('submit', function (e) { e.preventDefault(); saveEngine(); return false; });
                ApiClient.getPluginConfiguration(pluginId).then(function (c) {
                    config = c;
                    disabled = (c.DisabledItemIds || []).map(norm);
                    loadEngine();
                    renderTree();
                    showPill('libraries');
                    Dashboard.hideLoadingMsg();
                });
            });
        })();
    </script>
</div>
</body>
</html>
```

- [ ] **Step 2: Build (confirms the embedded config page still resolves)**

Run: `dotnet build src/MetaHub.Jellyfin/MetaHub.Jellyfin.csproj -c Release --nologo`
Expected: `0 Fehler`.

- [ ] **Step 3: Commit**

```bash
git add src/MetaHub.Jellyfin/Configuration/configPage.html
git commit -m "Rewrite config page: SkipMe.db-style per-item library manager"
```

---

## Task 5: Ship + live-verify on the test server

**Files:** none (CI + deploy).

- [ ] **Step 1: Open the PR and merge after CI is green**

```bash
git push -u origin claude/config-skipme-style
gh pr create --repo Kuschel-code/JDB --base main --head claude/config-skipme-style \
  --title "SkipMe.db-style config: per-item enable/disable" \
  --body "Per-item (library/series/season/movie) enable/disable with instant-saving toggles, cover art and filter; providers gate on the disabled set. Spec: docs/superpowers/specs/2026-06-05-metahub-config-skipme-style-design.md"
```
Wait for `gh pr checks <n> --repo Kuschel-code/JDB` → `build-test pass`, then
`gh pr merge <n> --repo Kuschel-code/JDB --merge --delete-branch`.

- [ ] **Step 2: Tag a release**

```bash
git fetch origin main -q
git tag v0.1.5 origin/main
git push origin v0.1.5
```
Wait: `gh run list -R Kuschel-code/JDB --workflow=release.yml -L 1` → `completed/success`. Then
verify the manifest+asset MD5 match (API, uncached) as in prior releases.

- [ ] **Step 3: Update the plugin on the test server**

Jellyfin `http://192.168.178.113:30013` (user `Jellyfin` / pw `18`). Using the context-mode JS
executor (curl is blocked): authenticate (`POST /Users/AuthenticateByName`), then
`POST /Packages/Installed/MetaHub?version=0.1.5.0`, wait ~10 s, `POST /System/Restart`, poll
`GET /System/Info/Public` until 200. Confirm the log shows `Loaded plugin: "MetaHub" "0.1.5.0"`
and no "App needs to be restarted" loop.

- [ ] **Step 4: Verify the config page live with Playwright**

Navigate to `http://192.168.178.113:30013/web/#/configurationpage?name=MetaHub`. Verify:
1. The **Libraries** pane lists libraries (e.g. "Anime") with a master toggle.
2. Expanding "Anime" lazy-loads series rows with cover thumbnails.
3. Toggling a series **off** persists: re-read `GET /Plugins/{guid}/Configuration` and assert the
   series' id (norm "N" form) is now in `DisabledItemIds`; toggling it back **on** removes it.
4. The **Engine** pane loads existing values and its **Save** button still works.

- [ ] **Step 5: (Optional) confirm gating end-to-end**

Disable one series in the UI, then in Jellyfin refresh that series' metadata ("replace all") and
confirm from the Jellyfin log that MetaHub did not return data for it; re-enable afterwards.

---

## Self-review notes

- **Spec coverage:** opt-out `DisabledItemIds` (T1); gate + ancestry + fail-open (T2); provider
  wiring incl. the `BaseItem` image overload (T3); full UI with pills/filter/lazy-tree/instant-save
  (T4); live verification incl. gating (T5). The pure gate core is unit-tested (T2).
- **Known environmental caveat:** two pre-existing SQLite tests fail on Windows only
  (temp-file delete lock); they pass on CI Linux — do not "fix" them here.
- **Type consistency:** `IsServed(ItemLookupInfo, string, PluginConfiguration)`,
  `IsServed(BaseItem, string, PluginConfiguration)` and
  `IsBlocked(IEnumerable<Guid>, IReadOnlyCollection<string>)` are used identically in the gate
  and all three providers; `work.MediaType` is a string everywhere.
