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
  MetaHub.Api             ASP.NET Core Minimal API
tests/
  MetaHub.Tests           Unit tests (parser + ingest)
docs/
  DATA_SOURCES.md         Curated provider/dataset catalogue
```

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

## API endpoints

| Method | Route                                  | Purpose                                   |
|--------|----------------------------------------|-------------------------------------------|
| GET    | `/health`                              | Liveness                                  |
| GET    | `/api/work/{id}`                       | Canonical record                          |
| GET    | `/api/work/{id}/images?type=poster`    | Artwork for a work                        |
| GET    | `/api/series/{id}/episodes`            | Episodes of a series/anime                |
| GET    | `/api/lookup?source=tmdb&id=12345`     | Resolve by external id                    |
| GET    | `/api/search?type=anime&q=...`         | Title search                              |
| POST   | `/api/identify`                        | Hash/fingerprint/ISBN → work + episode    |
| POST   | `/api/admin/ingest/anime`              | Trigger the anime ingest                  |

## Roadmap

- [x] **M1** Skeleton: solution, PostgreSQL, EF migrations, `Work`/`ExternalId`/`MediaFile`
- [x] **M2** Anime ingest: manami + Fribb → master data + cross-IDs
- [ ] **M3** Anime identification: ED2K hashing + AniDB file lookup (Shoko core)
- [ ] **M4** Enrichment v1: AniList + Jikan end-to-end (Polly + cache)
- [ ] **M5** API + NFO export, first Jellyfin test
- [ ] **M6** More media types: movies/series, music, books
- [ ] **M7** Jellyfin metadata/image provider plugin
- [ ] **M8** Conflict resolution, image scoring, i18n, monitoring

## Tests

```bash
dotnet test
```

## Legal

For personal use. Respect each provider's ToS and rate limits (User-Agent + contact
where required, e.g. MusicBrainz/AniDB). Cache aggressively; do not re-host aggregated
data or images publicly. See the concept document for details.
