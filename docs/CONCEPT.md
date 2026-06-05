# MetaHub — concept

A self-hosted media metadata aggregator (.NET / C#) that **exactly identifies** local
media files (Shoko principle: hash/fingerprint instead of filename guessing) and merges
metadata for **music, movies, series, anime and books** from multiple official sources —
normalized, cached, and served via its own API (and a Jellyfin plugin).

## 1. Goal

One service that produces a **canonical, unified view per item** — fed by multiple
providers, cross-linked by ID, cached locally — so Jellyfin and other clients get
consistent, rich metadata and artwork without querying every external API on each scan.

## 2. Scope

Included media types: **Music, Movies, Series, Anime, Books**.

Core capability (Shoko principle): local files are **exactly identified** — by file hash
(anime → AniDB), acoustic fingerprint (music → AcoustID/MusicBrainz) or identifier
(books → ISBN). Only then is metadata aggregated. This raises match rates from "guess the
filename" to "file reliably recognized" (Shoko reaches ~99%).

**Explicitly excluded:** unlicensed streaming/download sources. MetaHub is a *metadata*
aggregator, not a source finder for streams. Identification only determines the *identity*
of files the user already owns; no content is fetched and no acquisition sources are
provided.

## 3. The four building blocks

1. **Identification** — reliably recognizes a concrete local file as a specific
   work/episode (hash/fingerprint/identifier) and yields the authoritative external ID.
2. **Ingest** — loads finished mapping datasets and creates master records + external IDs.
3. **Enrichment** — background workers per media type query the right APIs, unify fields,
   and store locally.
4. **API layer** — serves the aggregated data; the Jellyfin plugin, optional Web UI and
   NFO export hang off it.

## 4. Exact identification (per media type)

- **Anime → file hash vs AniDB.** Compute ED2K (optionally CRC32) per file; query the
  AniDB file API which knows individual release files (fansub group, version) → "this file
  = anime X, episode Y". AniDB UDP API is strictly limited, requires client registration,
  anime only — cache results permanently, never ask twice.
- **Music → acoustic fingerprint.** Chromaprint (`fpcalc`) → AcoustID API → MusicBrainz
  recording IDs. Works even with chaotic tags/filenames.
- **Books → ISBN.** ISBN-13 uniquely identifies an edition; read it from file metadata
  (EPUB/PDF) or folder structure → Open Library / Google Books. Fallback: title+author.
- **Movies & series → no universal hash.** Identification via cleaned filename
  (title, year, SxxEyy) → TMDB/TVDB. Optional OpenSubtitles MovieHash as an extra signal.

### Flow

1. Discover file → quick check against `MediaFile` (hash/path known?) → done if so.
2. Otherwise build an identifier (ED2K / fingerprint / ISBN / name parse).
3. Resolve against the identification API → authoritative external ID.
4. Link `MediaFile` to `Work` (and `Episode`); cache the identifier.
5. If the `Work` is unknown → enqueue for ingest/enrichment.

## 5. Foundation: ID mapping (don't build it yourself)

The hard part is **linking the same work across providers** (MAL ≠ AniList ≠ AniDB …).
Ready-made datasets solve this:

- **manami-project/anime-offline-database** — ~35k anime with cross-refs (MAL, AniDB,
  AniList, Kitsu, …); weekly releases. License: ODbL/AGPL.
- **Fribb/anime-lists** — adds TVDB/TMDB/IMDb IDs merged via the AniDB ID — exactly what
  Jellyfin/artwork needs.
- For film/series/music/books the primary IDs (TMDB, MBID, ISBN) are de-facto standards;
  Wikidata bridges across domains.

See [`DATA_SOURCES.md`](./DATA_SOURCES.md) for the full provider/dataset catalogue.

## 6. Unified data model

A common base (`Work`) plus media-type-specific detail blocks, a generic `ExternalId`
table for cross-IDs, and `Image`/`Credit`/`Genre`. The identification layer (`MediaFile`)
links local files to works/episodes. A raw/cache layer (`RawPayload`, `SourceFetchLog`)
stores original responses and rate-limit bookkeeping.

Key decisions:
- **Store raw payloads separately (JSONB):** re-run normalization later without new API calls.
- **Conflict resolution by source priority:** per-field ordering (e.g. plot: TMDB > AniList
  > Kitsu); "best image" by score (resolution, language, votes).
- **Multilingual fields** kept as JSONB maps (e.g. overview `{de, en, ja}`), default DE/EN.

The model is implemented in `src/MetaHub.Domain` and mapped in
`src/MetaHub.Infrastructure/MetaHubDbContext.cs`.

## 7. Pipelines

- **Identification** (new/changed files): quick check → build identifier → resolve
  (Polly-throttled) → link `MediaFile`; enqueue unknown works.
- **Ingest** (rare, scheduled): download current mapping release → upsert `Work` +
  `ExternalId` → enqueue new/updated works for enrichment.
- **Enrichment** (continuous, one worker per source): check cache/TTL → fetch via Polly
  (rate-limit, retry+backoff, circuit breaker) → store `RawPayload` → normalize → upsert.

Caching TTL by source/field: long for finished works (30–90d), short for ongoing
series/anime (1–7d), permanent for images (refresh only on source update).

## 8. Jellyfin integration

- **A) Jellyfin metadata plugin (cleanest):** implement `IRemoteMetadataProvider<T, …>`
  and `IRemoteImageProvider`, fetching only from MetaHub's own API (central caching,
  uniform quality, one plugin for all media types).
- **B) NFO/artwork export (no plugin):** write `*.nfo` + images next to media files for
  Jellyfin's NFO reader. Faster to start. **Recommendation:** prototype with B, move to A.

## 9. Legal / ToS

Personal use is the intended scope; redistribution of aggregated data is forbidden by
practically every API ToS — do not offer it publicly. Identification only determines the
identity of already-owned files. AniDB file API: client registration required, strict
limits — query defensively, cache permanently. AcoustID: API key required. MusicBrainz /
AniDB: User-Agent + contact mandatory, respect rate limits. Mind dataset licenses
(manami: ODbL/AGPL). Cache images locally for personal use; do not re-host externally.

## 10. Roadmap

M1 skeleton · M2 anime ingest · M3 ED2K + AniDB identification (Shoko core) ·
M4 enrichment v1 (AniList + Jikan) · M5 API + NFO export · M6 more media types ·
M7 Jellyfin plugin · M8 polish (conflict resolution, image scoring, i18n, monitoring).

**Status:** M1 + M2 implemented. See the [README](../README.md) for build/run instructions.
