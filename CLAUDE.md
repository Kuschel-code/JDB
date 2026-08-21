# MetaHub (JDB) — Jellyfin metadata aggregator plugin

.NET 9 · Jellyfin 10.11 plugin SDK · EF Core · xUnit. Builds one canonical record per work
from many providers, cross-links by ID, caches locally. **Metadata only.**

Depth lives in `docs/BRAIN.md` (architecture, decisions, session history) and
`docs/DATA_SOURCES.md` (every provider, why it's in or out). This file is the map.

## Commands

```bash
dotnet build MetaHub.sln -c Release            # 1 pre-existing warning, see below
dotnet test  MetaHub.sln -c Release            # 259 tests, all green
```

Two results that are **pre-existing, not something you broke**:

- The Release build emits one `EF1002` warning
  (`src/MetaHub.Infrastructure/DependencyInjection.cs:80`, `ExecuteSqlRaw`). CI treats it
  as a warning, not an error.
- `dotnet format MetaHub.sln --verify-no-changes` reports **~183 whitespace deviations**
  across `tests/` and `src/MetaHub.Jellyfin/`. Formatting has never been enforced — CI does
  not run `format` — so a wholesale reformat would be a large, unrelated diff. Don't fix
  them as a side effect of another change.

**Cloud containers ship without a .NET SDK.** The SDK belongs in the cloud environment's
**setup script** — that runs before Claude Code launches and its filesystem is snapshotted,
so later sessions start with it already installed. `.claude/hooks/session-start.sh` uses
that SDK when it finds one, installs a per-user copy when it doesn't, and warms the NuGet
cache either way.

Both need the environment's network policy to allow the Microsoft download hosts. As of
2026-08-20 this environment answers **403 for `builds.dotnet.microsoft.com`** — the Trusted
list contains `dotnet.microsoft.com` but without a `*.` prefix, so the subdomain
`dot.net` redirects to is not covered (`api.nuget.org` is allowed). Until the policy lists
`*.dotnet.microsoft.com`, sessions run without an SDK and the hook says so.

If `dotnet` is missing, **don't burn turns trying to install it** — verify through CI:
push the branch and read the `build-test` job. Locally, if only .NET 8/10 are installed,
set `DOTNET_ROLL_FORWARD=Major` so `net9.0` runs on the .NET 10 runtime.

## Layout

```
src/MetaHub.Domain          entities + enums          src/MetaHub.Enrichment  providers + WorkMerger
src/MetaHub.Infrastructure  EF Core DbContext         src/MetaHub.Api         standalone ASP.NET API
src/MetaHub.Ingest          manami + Fribb + ARM      src/MetaHub.Jellyfin    the plugin itself
src/MetaHub.Identification  ED2K/AniDB/FilenameParser tests/MetaHub.Tests     unit + SQLite tests
```

Two modes: **embedded** (plugin is the engine, local SQLite — what users run) and
**standalone** (ASP.NET API + PostgreSQL, plugin becomes a thin client). `MetaHubBackend`
picks per call. Almost every bug that reached users was embedded-only.

## Things that will bite you

- **`manifest.json` is owned by the release workflow.** It computes the real MD5 and
  prepends the entry when the tag is cut. A hand-written entry ships an empty checksum and
  a dead `sourceUrl`, which breaks plugin installation for everyone on the repo feed.
  Bump `version` in `src/MetaHub.Jellyfin/build.yaml` instead — that file also holds the
  changelog.
- **Releases are tag pushes**, not the Actions button: `git tag vX.Y.Z && git push origin vX.Y.Z`.
  The manual *Run workflow* button needs `actions: write`, which the integration token lacks.
- **Changing an entity means bumping the embedded schema.** Since v0.1.9.7 the embedded
  SQLite version derives itself from the model, but PostgreSQL still needs a real EF
  migration *plus* a regenerated snapshot. Shipping one without the other is what broke
  v0.1.9.2–v0.1.9.5: `no such column` on every existing database.
- **Never bundle host-boundary assemblies** in the plugin zip (DI, Logging, Options, EF
  Core, SQLite) — Jellyfin provides them and duplicates break plugin load. Ship only
  `MetaHub.*.dll`, `Npgsql*`, `Polly*`, `Microsoft.Extensions.Http.Polly.dll`, `build.yaml`.
- **SQLite can't `ORDER BY` a `DateTimeOffset`.** Already handled — but mirror any
  `Replace` chain between C# (`NormTitle`) and EF queries or fuzzy matching silently
  diverges.
- **targetAbi `10.11.0.0` + `net9.0`.** A net8/10.10 plugin is not broken, it is *invisible*
  in the 10.11 catalog, which looks like a packaging bug and isn't.
- **Keyed providers are inert without a key** (TMDB, fanart.tv, Annict, AniDB) and are
  fixture-tested only. "Provider returns nothing" usually means no key or a blocked CDN,
  not a parser bug.

## Conventions

- Merging is **additive**: genres, images and credits are never deleted. Default write mode
  is FillMissingOnly; per-field source priority lives in `WorkMerger`.
- `ExternalIdSource` is stored as text, so a new provider needs no schema change.
- New provider → also add it to the **Sources** toggles in the settings page and to
  `docs/DATA_SOURCES.md` with the reason it's in (or why a candidate was rejected).
- The embedded DB is a **rebuildable cache**, not user data. Wiping and rebuilding it on a
  schema change is the intended behaviour, not a fallback.

## Verifying a change

CI (`.github/workflows/ci.yml`) restores, builds Release and runs the full test suite on
every push. That is the only build available from a cloud session — treat a green
`build-test` as the gate, and note in the PR that runtime behaviour inside a real Jellyfin
was not exercised from here.
