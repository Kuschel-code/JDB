# Configuration reference

MetaHub has **two configuration surfaces**, separated by ownership:

| Surface | Where | Owns | Edited by |
|---------|-------|------|-----------|
| **Server engine** | `MetaHub.Api/appsettings.json` (+ env vars) | Database, dataset ingest, identification (AniDB), enrichment, scheduling | The server operator |
| **Jellyfin plugin** | Jellyfin admin → Plugins → MetaHub | How Jellyfin connects to MetaHub and which content/language it consumes | The Jellyfin admin |

The plugin is only a **client** of the MetaHub API — it cannot perform ingest,
identification or enrichment itself. To avoid confusion, engine settings are *not* editable
from the plugin; instead the plugin's **Server** tab shows them **read-only** by calling
`GET /api/config` (secrets are reported as booleans only).

Precedence (highest first): environment variables → `appsettings.{Environment}.json` →
`appsettings.json` → built-in defaults.

---

## Server engine (`appsettings.json`)

### Connection & startup

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:MetaHub` | `Host=localhost;Port=5432;Database=metahub;Username=metahub;Password=metahub` | PostgreSQL connection string. Also overridable via the `METAHUB_CONNECTION` env var. |
| `MetaHub:AutoMigrate` | `true` | Apply EF Core migrations on startup. |

### `AnimeIngest` — mapping datasets (M2)

| Key | Default | Description |
|-----|---------|-------------|
| `ManamiUrl` | manami minified release | Source URL for the anime-offline-database. |
| `FribbUrl` | Fribb anime-lists full | Source URL for the AniDB↔TVDB/TMDB/IMDb mapping. |
| `UserAgent` | `MetaHub/0.1 (+…)` | User-Agent for dataset downloads. |

### `AniDb` — exact identification (M3) · **secrets**

| Key | Default | Description |
|-----|---------|-------------|
| `Enabled` | `false` | Master switch. AniDB lookups are skipped when off. |
| `Host` / `Port` | `api.anidb.net` / `9000` | AniDB UDP API endpoint. |
| `ClientName` / `ClientVersion` | `metahub` / `1` | Your **registered** AniDB UDP client. |
| `Username` / `Password` | empty | 🔒 AniDB account credentials. |
| `MinRequestIntervalSeconds` | `4` | Minimum spacing between UDP packets (be conservative). |
| `ReceiveTimeoutSeconds` | `20` | UDP reply timeout. |

### `Enrichment` — providers, caching, conflict resolution (M4/M6/M8) · **secrets**

| Key | Default | Description |
|-----|---------|-------------|
| `WriteMode` | `FillMissingOnly` | `FillMissingOnly` keeps existing metadata and only fills gaps; `Overwrite` replaces fields with the highest-priority provider value. Genres/images are always additive. |
| `PreferredLanguage` | `de` | Boosts matching artwork during image scoring; localizes `?lang=` responses. |
| `TtlFinishedDays` | `30` | Cache TTL for finished works. |
| `TtlOngoingDays` | `1` | Cache TTL for ongoing works. |
| `UserAgent` | `MetaHub/0.1 (+…)` | User-Agent for all providers (required by MusicBrainz/AniDB etiquette). |
| `TmdbApiKey` | empty | 🔒 TMDB API key (movies/series; provider inert without it). |
| `GoogleBooksApiKey` | empty | 🔒 Google Books API key (optional). |

Provider priority (lower wins per field): AniList 10 · TMDB 15 · Jikan 20 (anime);
TMDB 15 (movies/series); MusicBrainz 10 (music); Open Library 10 · Google Books 20 (books).

### `Scheduler` — background jobs (M7/M8)

| Key | Default | Description |
|-----|---------|-------------|
| `Enabled` | `false` | Master switch for all scheduled jobs. |
| `EnrichmentEnabled` | `true` | Periodically enrich works. |
| `EnrichmentIntervalMinutes` | `360` | Enrichment cadence. |
| `EnrichmentBatchSize` | `100` | Works per enrichment run. |
| `EnrichmentDelayMs` | `1000` | Pause between works (rate-limit pacing). |
| `EnrichmentOnlyMissing` | `true` | Only enrich works that currently have no overview. |
| `IngestEnabled` | `false` | Periodically refresh the mapping datasets. |
| `IngestIntervalHours` | `168` | Ingest cadence (weekly). |
| `ScanEnabled` | `false` | Periodically scan library folders and identify new files. |
| `ScanIntervalMinutes` | `60` | Scan cadence. |
| `ScanMaxFilesPerRun` | `200` | Cap files processed per scan. |
| `AnimeLibraryPaths` | `[]` | Folders to scan for anime files. |
| `VideoExtensions` | mkv/mp4/avi/… | Extensions treated as video. |

### Environment variable overrides

Use `__` (double underscore) for nesting, e.g. in `docker-compose.yml`:

```yaml
environment:
  ConnectionStrings__MetaHub: "Host=db;Port=5432;Database=metahub;Username=metahub;Password=metahub"
  Enrichment__WriteMode: "Overwrite"
  Enrichment__TmdbApiKey: "..."
  AniDb__Enabled: "true"
  Scheduler__Enabled: "true"
```

---

## Jellyfin plugin (admin → Plugins → MetaHub)

Tabs: **Connection · Library · Server · About**.

### Connection

| Setting | Default | Description |
|---------|---------|-------------|
| MetaHub API URL | `http://localhost:8080` | Base URL of your MetaHub server. |
| API key | empty | Optional; sent as the `X-Api-Key` header. |
| Request timeout (seconds) | `30` | Per-request timeout. |
| *Test connection* | — | Pings `/health`. |

### Library

| Setting | Default | Description |
|---------|---------|-------------|
| Movies / Series / Anime / Music / Books | all on | Which media types this plugin provides metadata for. A disabled type returns no metadata/images even if MetaHub knows the work. |
| Preferred language | `de` | Passed to MetaHub as `?lang=` for localized overviews. |
| Fallback language | `en` | Used when the preferred language is unavailable. |

### Server (read-only)

Live view of `GET /api/config` — the engine settings above, fetched from the server.
Edit those on the server, then press **Reload from server**.

---

## Secrets handling

Never commit real secrets. Keep `appsettings.json` with empty secret fields and supply
the real values via environment variables or `appsettings.Production.json` (git-ignored).
The `GET /api/config` endpoint deliberately exposes only booleans
(`tmdbConfigured`, `credentialsConfigured`, …) for secret-bearing settings.
