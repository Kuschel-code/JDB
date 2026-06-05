# MetaHub config page — SkipMe.db-style per-item management

**Date:** 2026-06-05
**Status:** Approved design
**Component:** `MetaHub.Jellyfin` (plugin config page + metadata providers)

## Problem

MetaHub's plugin config page (`Configuration/configPage.html`) only exposes coarse,
library-*type* toggles (Movies / Series / Anime / Music / Books) and a single bottom **Save**
button. Toggling a checkbox does nothing until Save is pressed, which reads as "the setting
isn't saved". There is no way to enable/disable MetaHub for an individual library, series, or
season.

The Intro Skipper **SkipMe.db** settings page is the desired UX reference: a searchable list
grouped by library, each library/series/season as a card with cover art and an instant-saving
toggle, expandable to manage children, with the note *"Segments remain in the local database
but will not be surfaced to Jellyfin when disabled."*

## Goal

Redesign the whole MetaHub config page in the SkipMe.db style, with per-item enable/disable
down to the season level (plus movies), instant-saving toggles, cover thumbnails, and a filter
search. Disabling an item means MetaHub stops *serving* metadata/artwork for it to Jellyfin;
the aggregated data stays in MetaHub's SQLite database.

## Semantics (decided)

- **Opt-out model.** Everything is enabled by default. Only *disabled* item IDs are persisted.
  An empty disabled-set ⇒ MetaHub serves everything (current behaviour).
- **OFF = not surfaced to Jellyfin.** When an item (or any ancestor) is disabled, MetaHub's
  providers return an empty result for it, so Jellyfin falls back to its other providers. The
  embedded SQLite data is untouched (enrichment/ingest still populate it).
- **Inheritance.** Disabling a library or series stores only that one ancestor ID; all its
  children are treated as disabled without enumerating them. Keeps the persisted set tiny even
  for libraries with thousands of items.
- The existing library-*type* toggles (`EnableMovies` … `EnableBooks`) remain as a coarse
  pre-filter, evaluated before the per-item check.

## Architecture

### 1. Configuration model — `PluginConfiguration`

Add:

```csharp
/// <summary>Jellyfin item GUIDs (libraries, series, seasons, movies) for which MetaHub must
/// NOT surface metadata. Empty = everything enabled (opt-out). Children inherit a disabled
/// ancestor, so only the highest disabled node is stored.</summary>
public string[] DisabledItemIds { get; set; } = System.Array.Empty<string>();
```

GUIDs are stored in Jellyfin's `"N"` (dash-less) format for stable comparison.

### 2. Gating — `MetaHubItemGate`

A new helper in `MetaHub.Jellyfin`, constructed with `ILibraryManager`. It exposes two
overloads sharing one disabled-set check:

```csharp
// Series / Movie providers: resolve the item from its provider ids first.
bool IsServed(ItemLookupInfo info, MediaType type, PluginConfiguration config)
// Image provider: the BaseItem is already in hand — no ILibraryManager lookup needed.
bool IsServed(BaseItem item, MediaType type, PluginConfiguration config)
```

Logic (for the `ItemLookupInfo` overload; the `BaseItem` overload starts at the resolved item):
1. **Type check** — if the work's media type is not enabled (`MetaHubMapping.IsMediaTypeEnabled`),
   return `false` (keeps current behaviour).
2. **Per-item check** — if `config.DisabledItemIds` is empty, return `true` (fast path).
   Otherwise resolve the Jellyfin item from `info.ProviderIds`
   (`ILibraryManager.GetItemList` with a provider-id query; take the first/strongest match).
   - If resolved: build the set `{ item.Id } ∪ item.GetAncestorIds()` (in `"N"` format) and
     return `false` if it intersects `DisabledItemIds`, else `true`.
   - If **not** resolved (e.g. first library scan, item not yet created): return `true`
     (nothing to gate yet; the user could not have toggled an item that isn't listed).

The gate is the single source of truth for "should MetaHub serve this item", reused by all
providers.

### 3. Providers

`MetaHubSeriesProvider`, `MetaHubMovieProvider`, `MetaHubImageProvider`:
- Inject `ILibraryManager` (host-provided) and `MetaHubItemGate`.
- Replace the inline `IsMediaTypeEnabled` check with `gate.IsServed(info, work.MediaType, config)`;
  when it returns `false`, return the empty `MetadataResult` / empty image list.

DI: register `MetaHubItemGate` in `MetaHubServiceRegistrator`. `ILibraryManager` is already
available from the Jellyfin host container.

### 4. Persistence / instant-save

No new API controller. The config page mutates `DisabledItemIds` on the in-memory configuration
object and calls `ApiClient.updatePluginConfiguration(pluginId, config)`, **debounced ~300 ms**
to coalesce rapid toggles. Because inheritance means a library/series master toggle stores a
single ancestor ID (not its children), payloads stay small. Last-write-wins is acceptable for a
single-admin settings page.

(Alternative, if atomic per-toggle saving is later wanted: a `POST /MetaHub/Items/Toggle`
elevated controller that adds/removes a single ID server-side. Out of scope for now.)

### 5. Frontend — `configPage.html`

Full restyle to the SkipMe.db look (dark cards, rounded rows, switch toggles) via a `<style>`
block plus Jellyfin classes. Three pills:

- **Libraries** (centrepiece, the per-item manager)
  - A filter search box. Typing filters loaded rows and, for deep matches, queries
    `ApiClient.getItems({ SearchTerm, IncludeItemTypes:'Series,Movie' })`.
  - For each library from `ApiClient.getVirtualFolders()`: a header row with the library name
    and a master toggle.
  - **Lazy** expansion: on expand, load that library's items page-by-page via
    `getItems({ ParentId, IncludeItemTypes:'Series,Movie', Recursive:false, StartIndex, Limit:100,
    Fields:'PrimaryImageAspectRatio' })`, rendering a row per item with a cover thumbnail
    (`ApiClient.getImageUrl(id,{ type:'Primary', maxHeight:80 })`), the name, a chevron, and a
    toggle. A "load more" / infinite scroll handles large libraries — never load all items at once.
  - A **series** row expands to its **seasons** (`getItems({ ParentId:seriesId, IncludeItemTypes:'Season' })`),
    each with a toggle.
  - Toggle = instant-save (debounced `updatePluginConfiguration`). A row whose ancestor is
    disabled renders as off/inherited.
- **Engine** — the existing Mode / embedded / remote-API / WriteMode / TMDB / AniDB scalar
  settings, restyled as cards, kept with a classic **Save** button (these are not per-item).
- **About** — restyled.

### 6. Testing

- Unit test `MetaHubItemGate` with a mocked `ILibraryManager`: an item enabled when neither it
  nor an ancestor is in the disabled set; disabled when itself, its series, or its library is in
  the set; enabled (fast path) when the set is empty; enabled when the item cannot be resolved.

## Files

| File | Change |
|------|--------|
| `src/MetaHub.Jellyfin/Configuration/PluginConfiguration.cs` | add `DisabledItemIds` |
| `src/MetaHub.Jellyfin/MetaHubItemGate.cs` | **new** gating helper |
| `src/MetaHub.Jellyfin/Providers/MetaHubSeriesProvider.cs` | use gate + inject `ILibraryManager` |
| `src/MetaHub.Jellyfin/Providers/MetaHubMovieProvider.cs` | use gate |
| `src/MetaHub.Jellyfin/Providers/MetaHubImageProvider.cs` | use gate |
| `src/MetaHub.Jellyfin/MetaHubServiceRegistrator.cs` | register gate |
| `src/MetaHub.Jellyfin/Configuration/configPage.html` | full redesign |
| `tests/MetaHub.Tests/MetaHubItemGateTests.cs` | **new** unit tests |

## Risks / open points

- **Provider-id resolution at refresh time.** `ItemLookupInfo` carries `ProviderIds` but not the
  Jellyfin item GUID/ancestry, so the gate resolves the item via `ILibraryManager`. Ambiguous
  matches (two items sharing a provider id) are rare; the gate takes the first match and
  fails *open* (serves) when nothing resolves, so a miss never wrongly hides data.
- **Large libraries.** The Libraries view must lazy-load and paginate; loading an entire anime
  library (tens of thousands of items) eagerly would hang the page.
- `MetaHubImageProvider`'s `GetImages` receives a `BaseItem`, which *does* expose `Id` /
  `GetAncestorIds()` directly — the gate accepts that path without an `ILibraryManager` lookup.

## Out of scope

- Crowd-sourced "Sync / Share" tabs (SkipMe.db-specific; MetaHub aggregates from providers, it
  doesn't share user data).
- Per-episode granularity.
- An atomic per-toggle save controller (debounced full-config save is used instead).
