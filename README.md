# JDB — MetaHub

**Jellyfin Meta DB** — a self-hosted media metadata aggregator (.NET / C#).

MetaHub builds a single canonical, unified view per media item by combining several
official providers, cross-linking them by ID and caching everything locally — so
Jellyfin (and other clients) get consistent, rich metadata and artwork without hammering
every external API on each scan.

Like [Shoko](https://shokoanime.com/), MetaHub **identifies local files exactly**
(file hash / acoustic fingerprint / identifier) instead of guessing from filenames, and
only then aggregates metadata.

> Scope: **metadata only**. MetaHub does not index or provide unlicensed
> streaming/download sources. It identifies files you already own and enriches them.

## Media types

Music · Movies · Series · Anime · Books

## Architecture

```
Local files ─► [0] Identification ─► file ↔ work link (ED2K/AniDB, AcoustID, ISBN, filename/TMDB)
Mappings    ─► [1] Ingest         ─► master data + cross-IDs (PostgreSQL)
External    ─► [2] Enrichment     ─► normalized fields + artwork (rate-limited, cached, Polly)
APIs        ─► [3] Web API        ─► Jellyfin plugin · Web UI · NFO export
```

## Tech stack

| Layer            | Choice                                  |
|------------------|-----------------------------------------|
| Runtime          | .NET 8 (LTS)                            |
| Web API          | ASP.NET Core Minimal APIs              |
| ORM / DB         | EF Core + PostgreSQL (JSONB)           |
| HTTP resilience  | `IHttpClientFactory` + Polly           |
| Logging          | `Microsoft.Extensions.Logging`         |
| Container        | Docker / docker-compose                |

## Project layout

```
src/
  MetaHub.Domain          Entities + enums (the unified data model)
  MetaHub.Infrastructure  EF Core DbContext, migrations, PostgreSQL mapping
  MetaHub.Ingest          M2 anime ingest (manami + Fribb) with Polly-backed HTTP
  MetaHub.Identification  M3 Shoko core: ED2K/MD4/CRC32 hashing + AniDB UDP client
  MetaHub.Enrichment      M4 enrichment: AniList + Jikan providers, merger, TTL cache
  MetaHub.Export          M5 NFO export (Jellyfin/Kodi-compatible *.nfo)
  MetaHub.Api             ASP.NET Core Minimal API
  MetaHub.Jellyfin        Jellyfin plugin: tabbed admin settings page (M7 head-start)
tests/
  MetaHub.Tests           Unit tests (parser + ingest)
docs/
  CONFIGURATION.md        Full settings reference (server engine + plugin)
  DATA_SOURCES.md         Curated provider/dataset catalogue
```

## Configuration

Settings live in two clearly separated places — the **server engine**
(`MetaHub.Api/appsettings.json`) and the **Jellyfin plugin** (admin UI, a client of the
API). See **[docs/CONFIGURATION.md](docs/CONFIGURATION.md)** for the full reference,
defaults, env-var overrides and the ownership model.

## Quick start

### With Docker (Postgres + API)

```bash
docker compose up --build
# API on http://localhost:8080  (Swagger UI at /swagger)
```

### Local development

```bash
# 1. Start a Postgres (or use docker compose up db)
# 2. Apply migrations + run the API
dotnet run --project src/MetaHub.Api
```

The connection string is read from `ConnectionStrings:MetaHub`
(or the `METAHUB_CONNECTION` env var). Migrations are applied on startup unless
`MetaHub:AutoMigrate` is set to `false`.

### Run the anime ingest (M2)

```bash
curl -X POST http://localhost:8080/api/admin/ingest/anime
```

This downloads the [manami-project/anime-offline-database](https://github.com/manami-project/anime-offline-database)
and [Fribb/anime-lists](https://github.com/Fribb/anime-lists) datasets, creates `Work`
master records and populates their cross-provider `ExternalId`s. Re-running is idempotent.

### Identify a local file (M3, Shoko core)

```bash
curl -X POST http://localhost:8080/api/files/identify \
  -H 'Content-Type: application/json' \
  -d '{"path": "/media/anime/episode.mkv"}'
```

This computes the file's **ED2K** hash (MD4-based) and, if AniDB is enabled, resolves it via
the AniDB UDP **FILE** command to the exact anime/episode, then links it to the matching
`Work`. The hash is cached so it is never recomputed.

### Enrichment write mode

Enrichment can either **only fill missing fields** (default, never touches existing metadata)
or **overwrite** with the highest-priority provider value. Set it globally via
`Enrichment:WriteMode` (`FillMissingOnly` | `Overwrite`) or per request with `?writeMode=`.
Batch enrichment can also target only works that have no metadata yet via `?onlyMissing=true`.
Genres and images are always additive (never removed).

### AniDB

AniDB is **disabled by default** and requires a registered UDP client plus account. Enable
it under the `AniDb` configuration section (respect AniDB's strict rate limits — the client
serializes requests and waits between packets):

```jsonc
"AniDb": {
  "Enabled": true,
  "ClientName": "yourclient",   // registered AniDB UDP client name
  "ClientVersion": 1,
  "Username": "...",
  "Password": "..."
}
```

### Scheduled scan

A built-in background scheduler can periodically refresh the mapping datasets, enrich works,
and scan library folders to identify new files (ED2K/AniDB). It is **disabled by default**;
configure it under the `Scheduler` section:

```jsonc
"Scheduler": {
  "Enabled": true,
  "EnrichmentEnabled": true,  "EnrichmentIntervalMinutes": 360, "EnrichmentOnlyMissing": true,
  "IngestEnabled": false,     "IngestIntervalHours": 168,
  "ScanEnabled": true,        "ScanIntervalMinutes": 60,
  "AnimeLibraryPaths": ["/media/anime"]
}
```

## API endpoints

| Method | Route                                  | Purpose                                   |
|--------|----------------------------------------|-------------------------------------------|
| GET    | `/health`                              | Liveness                                  |
| GET    | `/api/work/{id}?lang=de`               | Canonical record (localized overview)     |
| GET    | `/api/work/{id}/images?type=poster`    | Artwork for a work                        |
| GET    | `/api/series/{id}/episodes`            | Episodes of a series/anime                |
| GET    | `/api/lookup?source=tmdb&id=12345`     | Resolve by external id                    |
| GET    | `/api/search?type=anime&q=...`         | Title search                              |
| GET    | `/api/config`                          | Read-only engine config (secrets as bools) |
| GET    | `/api/work/{id}/nfo`                    | NFO XML for the work (Jellyfin/Kodi)      |
| POST   | `/api/identify`                        | Resolve an already-identified file by hash/path |
| POST   | `/api/files/identify`                  | ED2K-hash a local file + AniDB lookup (M3) |
| POST   | `/api/admin/ingest/anime`              | Trigger the anime ingest                  |
| POST   | `/api/admin/enrich/work/{id}`          | Enrich one work (AniList + Jikan)         |
| POST   | `/api/admin/enrich/anime`              | Batch-enrich anime works (paced)          |
| POST   | `/api/admin/export/nfo/{id}?dir=...`   | Write an NFO file to a directory          |
| GET    | `/api/admin/stats`                     | Counts (works by type, files, images, ...) |

## Roadmap

- [x] **M1** Skeleton: solution, PostgreSQL, EF migrations, `Work`/`ExternalId`/`MediaFile`
- [x] **M2** Anime ingest: manami + Fribb → master data + cross-IDs
- [x] **M3** Anime identification: ED2K hashing + AniDB file lookup (Shoko core)
- [x] **M4** Enrichment v1: AniList + Jikan end-to-end (Polly + cache)
- [x] **M5** API + NFO export, first Jellyfin test
- [x] **M6** More media types: movies/series (TMDB), music (MusicBrainz), books (Open Library + Google Books)
- [x] **M7** Jellyfin metadata/image provider plugin (`MetaHub.Jellyfin`, fetches from the API)
- [x] **M8** Conflict resolution (priority + write modes), image scoring, i18n (`?lang=`), Serilog + stats

## Jellyfin plugin

Install via the plugin **repository link** (Jellyfin → Dashboard → Plugins → Repositories → **+**):

```
https://raw.githubusercontent.com/Kuschel-code/JDB/main/manifest.json
```

Then open **Catalog → Metadata → MetaHub** and install.

### Embedded mode — no Docker, no server

By default the plugin runs **fully embedded** inside Jellyfin: a local **SQLite** database in
the plugin data folder, in-process identification/enrichment, and ingest/enrichment exposed as
Jellyfin **Scheduled Tasks** (Dashboard → Scheduled Tasks → *MetaHub: Update anime mappings* /
*MetaHub: Enrich metadata*). Datasets are pulled from GitHub. Nothing else to install.

First run:
1. Configure the plugin (**Plugins → MetaHub** → Mode / Library / Engine).
2. Run **MetaHub: Update anime mappings** once, then **MetaHub: Enrich metadata**.

Turn off **embedded** only if you want the plugin to talk to a separate MetaHub server (the
ASP.NET API / Docker setup below) instead. See [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

> The standalone server (ASP.NET API + PostgreSQL, or a self-contained binary with SQLite)
> remains available for non-Jellyfin clients, but is **not required** for the plugin.

> The link serves [`manifest.json`](manifest.json), which lists installable versions. It is
> populated automatically by the **Release** workflow: pushing a `v*` tag builds the plugin
> zip, attaches it to a GitHub Release, and adds the version (with its MD5 checksum) to the
> manifest. So the link becomes installable once this is merged to `main` and the first
> release tag is pushed.

## Tests

```bash
dotnet test
```

## Legal

For personal use. Respect each provider's ToS and rate limits (User-Agent + contact
where required, e.g. MusicBrainz/AniDB). Cache aggressively; do not re-host aggregated
data or images publicly. See the concept document for details.
