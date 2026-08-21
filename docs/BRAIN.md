# BRAIN — MetaHub project memory

Durable context for future sessions. This is the project's "brain": what it is, how it's
built, decisions made, gotchas, and what's left. Update it as the project evolves.

_Last updated: 2026-08-20 · current release: v0.1.9.8_

`CLAUDE.md` in the repo root is the short version Claude loads every session (commands,
gotchas, conventions). This file is the long-form memory behind it — keep both in sync
when a fact here changes.

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
- **Cloud containers ship without a .NET SDK**, so the environment's **setup script**
  installs it (that runs before Claude Code and its filesystem is cached, unlike a
  SessionStart hook). This needs the environment's network policy to allow
  `*.dotnet.microsoft.com` — the Trusted list has `dotnet.microsoft.com` without a `*.`
  prefix, so `builds.dotnet.microsoft.com`, where `dot.net` redirects, is not covered.
  With that allowed, `dotnet build` and `dotnet test` work from a cloud session
  (verified 2026-08-21, SDK 9.0.317). Without it, CI is the only gate. On a machine that
  has only .NET 8/10 installed, set `DOTNET_ROLL_FORWARD=Major` so net9 runs on .NET 10.
- **Plugin zip bundle list** (Jellyfin provides EF Core/SQLite/Microsoft.Extensions, so we
  only ship what it doesn't): `MetaHub.*.dll`, `Npgsql.dll`,
  `Npgsql.EntityFrameworkCore.PostgreSQL.dll`, `Polly.dll`, `Polly.Extensions.Http.dll`,
  `Microsoft.Extensions.Http.Polly.dll` + `build.yaml`. Bundling host-boundary assemblies
  (DI/Logging/Options/EF) breaks the plugin — never bundle those.
- **Releases:** push a version tag — `git tag vX.Y.Z && git push origin vX.Y.Z` — and the
  Release workflow (`on: push: tags: v*`) builds the zips + plugin, creates the GitHub release,
  and updates `manifest.json` on `main`. This is how every release (v0.1.7.x–v0.1.9.8) was cut;
  it needs only push access. (The manual **Actions → Run workflow** button needs `actions: write`,
  which the integration token lacks — but the tag-push path does not, so prefer it.)
- **Plugin install link (stable):**
  `https://raw.githubusercontent.com/Kuschel-code/JDB/main/manifest.json`
- **`manifest.json` belongs to the release workflow, not to you.** It computes the real
  MD5 and prepends the entry when the tag is cut. A hand-added entry carries an empty
  checksum and a dead `sourceUrl` and breaks installation for everyone on the repo feed
  until the tag lands (learned the hard way at v0.1.9.2). Bump the version and write the
  changelog in `src/MetaHub.Jellyfin/build.yaml` instead.
- **Entity change = schema change in both modes.** Since v0.1.9.7 the embedded SQLite
  schema version derives itself from the model, so it can no longer be forgotten; but
  PostgreSQL still needs a real EF migration **plus** a regenerated snapshot. Shipping the
  model change without the migration is exactly what broke v0.1.9.2–v0.1.9.5
  (`no such column` on every existing database).
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
- **Name matching, finished** (v0.1.8.3–v0.1.8.8): synonyms/alternate titles are indexed and
  searched; bracketed titles match across `[…]`/`(…)`/none; anime films and OVAs resolve by
  folder name (no truncation at `I.`/`II.`, `Movie:` prefix tolerated, media type taken from
  the library name); a sequel whose only marker lives in a synonym ("Saiki K. Reawakened")
  no longer collapses onto the base season. Folder matches now trigger on-demand enrichment
  just like id matches.
- **More sources** (v0.1.8.7–v0.1.9.0): Kitsu (no key), fanart.tv (optional free key —
  posters, backgrounds, clear logos, language-aware), Shikimori (no key), plus five more
  anime cross-ids (AniSearch, Notify, LiveChart, Annict, Syobocal). TMDB is fetched in the
  configured language. Genres and studios are written onto the Jellyfin item. Every source
  has its own on/off toggle in the settings **Sources** section.
- **AniDB HTTP provider, phase 1** (v0.1.9.2–v0.1.9.6): titles, episodes, characters and
  artwork from the HTTP anime endpoint (gzip XML, rate-limited, ban-safe, 10 MB cap,
  oversized responses rejected rather than cached truncated). Episode sync stores AniDB
  episode id, kind (Regular/Special/Credit/Trailer/Parody) and raw episode number; work
  status is derived from air dates and drives the refresh TTL. `SearchTitles` is enriched
  with provider title translations after each merge, so name matching improves every run.
- **Audit hardening** (v0.1.8.4, v0.1.9.1, v0.1.9.7, v0.1.9.8): WAL + `busy_timeout` and
  soft-failing reads (concurrent scans no longer drop metadata with "database is locked");
  one provider's failure no longer aborts a work's whole enrichment, and a timing-out
  provider no longer aborts an on-demand Jellyfin refresh; payloads are cached even when
  every parse fails, so the next run doesn't re-hit rate limits; TMDB/fanart.tv/Google Books
  no longer log their API key; ED2K exact-multiple files append the empty trailing chunk
  (matches AniDB's canonical hash); AniDB UDP re-auths on 501/502/506 and backs off on 555;
  the standalone API gates on `X-Api-Key` when `MetaHub:ApiKey` is set; MusicBrainz's ~1 req/s
  limit is actually enforced; per-row genre/dataset lookups are batched.

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
- **Overview language**: `PreferredLanguage` defaults to English since v0.1.9.8. Season names
  localize, **descriptions still don't** — `OverviewTranslations` is not filled by any
  provider that has translated synopses. This is the open translation gap; see
  `docs/DATA_SOURCES.md` § *Translation coverage*.
- **Source candidates investigated and rejected** (don't re-research these):
  *aniSearch* — no usable public read API; the only working method is HTML scraping, which
  its own integration's issue tracker documents getting IP-banned for.
  *Bangumi / Douban and other Chinese sources* — deprioritized: PRC content moderation is a
  real accuracy risk for something whose job is to be canonical.
  *Kitsu* — kept as a low-priority fallback, but platform development has stalled (no
  roadmap since 2026); don't expect a fast fix if it breaks.

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
- Name matching completed (synonyms, brackets, films/OVAs, sequel markers) — v0.1.8.3–v0.1.8.8.
- Kitsu / fanart.tv / Shikimori providers, per-source toggles, TMDB language — v0.1.8.7–v0.1.9.0.
- AniDB HTTP provider phase 1 + the schema-migration lesson — v0.1.9.2–v0.1.9.7.
- Reliability/performance audit + provider ordering — v0.1.9.8.
- Source research filed in DATA_SOURCES.md: aniSearch (no API), regional China/Korea/Japan
  connections (China deprioritized), Kitsu's stalled development, the translation gap.
- Released through v0.1.9.8. Test suite: **259 tests, all green** (41 files; the 180 figure
  in earlier notes counted `[Fact]`/`[Theory]` attributes and missed the `[InlineData]`
  cases). Verified 2026-08-21 on a cloud session with the SDK installed.
