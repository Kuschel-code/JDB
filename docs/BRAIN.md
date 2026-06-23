# BRAIN — MetaHub project memory

Durable context for future sessions. This is the project's "brain": what it is, how it's
built, decisions made, gotchas, and what's left. Update it as the project evolves.

_Last updated: 2026-06-23 · current release: v0.1.8.2_

## What this is

**JDB / MetaHub** — a self-hosted media metadata aggregator (.NET) for Jellyfin. It builds
one canonical record per work (music, movies, series, anime, books) from many providers,
cross-links them by ID, caches locally, and serves Jellyfin consistent metadata + artwork.
Shoko principle: identify files exactly (hash/fingerprint/ISBN), don't guess from names.
**Metadata only** — no streaming/download sources.

## Two deployment modes

- **Embedded plugin (default, recommended):** the Jellyfin plugin *is* the engine — local
  **SQLite** DB in the plugin data folder, in-process ingest/identification/enrichment,
  exposed as Jellyfin **Scheduled Tasks**. No Docker, no server, no DB install.
- **Standalone server (optional):** ASP.NET API + PostgreSQL (or SQLite). The plugin can run
  as a thin client against it. For non-Jellyfin clients / shared multi-app setups.

## Solution layout

```
src/MetaHub.Domain          entities + enums (Work, ExternalId, MediaFile, Person, Credit, …)
src/MetaHub.Infrastructure  EF Core DbContext — provider-agnostic (SQLite or PostgreSQL)
src/MetaHub.Ingest          anime ingest: manami + Fribb + ARM (Japanese ids)
src/MetaHub.Identification  ED2K/MD4/CRC32 hashing, AniDB UDP client, FilenameParser
src/MetaHub.Enrichment      providers + WorkMerger + EnrichmentService/Runner + JikanEpisodeSync
src/MetaHub.Export          NFO export (Jellyfin/Kodi)
src/MetaHub.Api             ASP.NET Core Minimal API (server mode)
src/MetaHub.Jellyfin        the plugin: backend, providers, scheduled tasks, settings page
tests/MetaHub.Tests         unit + SQLite integration tests
docs/                       CONCEPT.md, CONFIGURATION.md, DATA_SOURCES.md, BRAIN.md (this)
```

## Critical environment facts (don't relearn these)

- **Target: .NET 9 + Jellyfin 10.11** (`Jellyfin.Controller` 10.11.x ships as `net9.0`).
  A net8/10.10 plugin is **invisible in the 10.11 catalog** (ABI mismatch). targetAbi
  `10.11.0.0`, framework `net9.0`. EF Core / Microsoft.Extensions aligned to **9.0.11**
  (matches Jellyfin's stack); Npgsql EF provider stays 9.0.4 (latest, compatible).
- **This sandbox has only .NET 8 + .NET 10 SDKs** (no .NET 9). Build/test with
  `DOTNET_ROLL_FORWARD=Major` so net9 runs on the .NET 10 runtime.
- **Plugin zip bundle list** (Jellyfin provides EF Core/SQLite/Microsoft.Extensions, so we
  only ship what it doesn't): `MetaHub.*.dll`, `Npgsql.dll`,
  `Npgsql.EntityFrameworkCore.PostgreSQL.dll`, `Polly.dll`, `Polly.Extensions.Http.dll`,
  `Microsoft.Extensions.Http.Polly.dll` + `build.yaml`. Bundling host-boundary assemblies
  (DI/Logging/Options/EF) breaks the plugin — never bundle those.
- **Releases:** push a version tag — `git tag vX.Y.Z && git push origin vX.Y.Z` — and the
  Release workflow (`on: push: tags: v*`) builds the zips + plugin, creates the GitHub release,
  and updates `manifest.json` on `main`. This is how every release (v0.1.7.x–v0.1.8.2) was cut;
  it needs only push access. (The manual **Actions → Run workflow** button needs `actions: write`,
  which the integration token lacks — but the tag-push path does not, so prefer it.)
- **Plugin install link (stable):**
  `https://raw.githubusercontent.com/Kuschel-code/JDB/main/manifest.json`
- **SQLite gotcha:** can't `ORDER BY DateTimeOffset` — already handled; mirror any Replace
  chains between C# (`NormTitle`) and EF queries or fuzzy matching breaks.

## Key design decisions

- `MetaHubBackend` takes `Func<PluginConfiguration>` (not static `Plugin.Instance`) → testable;
  registrator supplies the accessor.
- `IMetaHubBackend` abstracts embedded vs remote; a fresh DI scope per call (providers may be
  singletons in Jellyfin, DbContext is scoped).
- `WorkMerger`: per-field source priority + write mode (FillMissingOnly default / Overwrite);
  genres, images and credits are always **additive, never deleted**.
- Provider priority (lower wins): AniList 10 · TMDB 15 · Jikan 20 · Annict 30 (anime);
  TMDB 15 (movie/series); MusicBrainz 10; Open Library 10 · Google Books 20.
- ExternalIdSource is stored as text → new providers need no schema change.
- Embedded schema via `EnsureCreated` (no migrations); server uses the Npgsql migration.

## What works (resolved during these sessions)

- M1–M8 complete (skeleton → ingest → ED2K/AniDB → enrichment → NFO → all media types →
  Jellyfin providers → conflict resolution/scoring/i18n/Serilog/stats).
- Embedded mode (no Docker), settings page (native Jellyfin look, tabs), per-item opt-out
  (`DisabledItemIds`) via `MetaHubItemGate`, XSS-safe library tree.
- Season + Episode metadata providers; image provider supports series/movie/season (episode
  stills TODO). Episode data filled by `JikanEpisodeSync`.
- Cast & crew (Besetzung/Mitwirkende): AniList voice actors+staff, TMDB cast+crew → Person/
  Credit → Jellyfin `PersonInfo` + NFO `<actor>/<director>/<credits>`.
- Japanese sources: Annict provider (token) + ARM mapping (Annict/Syoboi ids) + Japanese
  episode titles when preferred language = `ja`.
- Title/folder-name fallback: when no provider id matches, resolve by lookup name then the
  cleaned folder name; **library-aware** (`MetaHubLibraryClassifier`) and
  punctuation-insensitive (folder "227" → anime "22/7"); year disambiguation; last resort =
  folder name as title (so titles are never empty/garbage like "Ö").
- Posters at ingest: manami per-anime poster/thumbnail stored during "Update anime mappings"
  so every work has art immediately (enrichment covers override later by score).
- Scheduled tasks report real progress.
- Project logo at `assets/logo.png` → plugin catalog (`manifest.json` imageUrl) + README.
- **Language-aware titles** (v0.1.7.9): `Work.TitleTranslations` (JSONB, like OverviewTranslations);
  providers capture en/ja titles even when the romaji `CanonicalTitle` is kept (FillMissingOnly),
  so Jellyfin can show the viewer's-language name instead of romaji.
- **"Apply metadata to library" task** (v0.1.8.0): pushes MetaHub titles/data onto existing
  library items (beyond Jellyfin's own refresh).

## Known limitations / TODO

- **Not validated inside a real Jellyfin runtime** from here (sandbox can't run Jellyfin);
  parsing/merging/SQLite paths are unit-tested. Verify provider visibility + image fetch on
  the real server after each release.
- **Episode stills**: image provider lists episodes but supplies none yet (add TMDB/AniDB
  episode images).
- **AniDB / AcoustID / live AniList/TMDB/Annict** need keys/credentials/network — parsers are
  fixture-tested only; keyed providers are inert without a key.
- **Image download blocking**: if posters don't appear, check whether the Jellyfin host can
  reach the source CDN (e.g. cdn.myanimelist.net).
- **Music identification (AcoustID/Chromaprint/fpcalc)** not implemented (M6 left it as a
  parser/provider; no fingerprinting yet).
- Remote-mode title lookup endpoint not exposed (ResolveByNameAsync is embedded-only).

## Release checklist

1. Merge the PR to `main` (CI `build-test` must be green).
2. Tag and push the next `vX.Y.Z` (`git tag vX.Y.Z && git push origin vX.Y.Z`) → Release workflow runs. Then verify asset MD5 == manifest + DLL version == tag.
3. In Jellyfin: update plugin → **restart**.
4. Library → Manage → enable **MetaHub** under metadata + image providers (series/season/
   episode), move up.
5. Run task **MetaHub: Update anime mappings**, then **MetaHub: Enrich metadata**.
6. Per title: refresh metadata (tick "replace images" to pull posters).

## Session history (high level)

- Built M1–M8 + concept/docs; settings architecture (client/server split, read-only server view).
- Added GitHub release workflow (zips on tag) + plugin repository manifest + install link.
- Added embedded plugin mode (SQLite, in-process engine, scheduled tasks).
- Retargeted everything to .NET 9 / Jellyfin 10.11 — fixed "plugin not in catalog".
- Restructured settings page repeatedly (tabs → native look) per user feedback.
- Season/episode providers; Japanese sources (Annict/ARM/Jikan episodes).
- Cast & crew end to end.
- Title/folder-name + library-aware fuzzy matching; posters at ingest; project logo.
- Language-aware titles (TitleTranslations); "Apply metadata to library" task.
- Released through v0.1.8.2.
