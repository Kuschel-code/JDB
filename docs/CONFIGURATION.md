# Configuration reference

## Deployment modes

MetaHub runs in one of two modes, chosen in the plugin (**Plugins → MetaHub → Mode**):

- **Embedded (default, no Docker):** the Jellyfin plugin *is* the engine. It uses a local
  **SQLite** database (`<jellyfin-data>/metahub/metahub.db`), runs identification/enrichment
  in-process, and exposes ingest/enrichment as Jellyfin Scheduled Tasks. In this mode the
  engine settings below live in the **plugin configuration**, not in `appsettings.json`.
- **Remote/server:** a standalone MetaHub server (ASP.NET API + PostgreSQL, or a self-contained
  binary with SQLite) runs the engine, and the plugin is a thin client. Engine settings live in
  the server's `appsettings.json`.

The tables below describe the server `appsettings.json`. In embedded mode the same concepts are
configured from the plugin's **Library** and **Engine** tabs (TMDB/Google Books keys, write
mode, preferred language, AniDB credentials).

---

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
| `AnnictToken` | empty | 🔒 Annict personal access token (Japanese anime metadata; optional). |
| `CustomSources` | empty | Metadata databases you host yourself — see below. |
| `CustomSourceMode` | `Prefer` | How they rank against the built-ins: `Prefer` (yours wins), `Fallback` (only fills gaps), `Auto` (per entry — substantial wins, stubs step behind). |

Provider priority (lower wins per field): AniList 10 · TMDB 15 · Jikan 20 (anime);
TMDB 15 (movies/series); MusicBrainz 10 (music); Open Library 10 · Google Books 20 (books);
Annict 30 (Japanese). Self-hosted sources default to **5**, ahead of all of them.

#### `Enrichment:CustomSources` — your own databases

Each entry is a folder of JSON/NFO files or your own HTTP endpoint, matched by **title**
(canonical, original or any known translation) rather than by provider id:

```json
"CustomSources": [
  { "Name": "My NAS", "Kind": "Folder", "Location": "/mnt/nas/metadata", "Priority": 5 },
  { "Name": "Home API", "Kind": "Http", "Location": "https://meta.lan/metahub", "ApiKey": "s3cret", "Priority": 3 }
]
```

| Key | Default | Description |
|-----|---------|-------------|
| `Name` | derived | Display name (used in logs). Defaults to the folder name or URL host. |
| `Kind` | `Folder` | `Folder` (files on disk) or `Http` (your own endpoint). |
| `Location` | — | Absolute folder path or base URL. Required. |
| `Priority` | by mode | Optional per-source override, lower wins; the built-in providers sit at 10–30. Omit it to follow `CustomSourceMode` (`Prefer` → 5, `Fallback` → 1000). |
| `ApiKey` | empty | 🔒 Sent as the `X-Api-Key` header (HTTP sources only). |

Responses are **not** cached as raw payloads — a local folder is already fast, and a
self-hosted endpoint is the user's own server. The document format (JSON fields, NFO support,
folder layout, endpoint contract) is documented in the [README](../README.md#your-own-metadata-databases).

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

### Metadata sources

| Setting | Default | Description |
|---------|---------|-------------|
| TMDB / Google Books / Annict keys | empty | 🔒 Optional; a source without its key is skipped. |
| Your own databases | empty | Self-hosted sources, one per line: `Name \| Location \| Priority \| ApiKey`. The location must be absolute — a URL is queried as an endpoint, anything else is read as a folder. Applied after a Jellyfin restart. See the [README](../README.md#your-own-metadata-databases). |
| When sources disagree | `Prefer my database` | Whether your data wins over the built-in sources, only fills their gaps, or is judged per entry (*Auto*). |

### Server (read-only)

Live view of `GET /api/config` — the engine settings above, fetched from the server.
Edit those on the server, then press **Reload from server**.

---

## Secrets handling

Never commit real secrets. Keep `appsettings.json` with empty secret fields and supply
the real values via environment variables or `appsettings.Production.json` (git-ignored).
The `GET /api/config` endpoint deliberately exposes only booleans
(`tmdbConfigured`, `credentialsConfigured`, …) for secret-bearing settings.
