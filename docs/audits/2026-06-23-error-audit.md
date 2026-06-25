# MetaHub/JDB — Error Audit (2026-06-23)

Multi-agent audit: 10 subsystem dimensions reviewed, every finding adversarially verified by 2
skeptics, then triaged + spot-verified by hand. Baseline before audit: build clean (0 warnings),
135/135 tests green. 43 raw findings → 33 survived verification → triaged below.

## Triage of the two "critical" findings (hand-verified)

### ❌ FALSE POSITIVE — "Plugin omits Polly.Core.dll → tombstone"
The agents assumed **Polly 8.6.5** (which splits into Polly + Polly.Core). The project actually
references `Microsoft.Extensions.Http.Polly` **9.0.4**, which pulls **Polly 7.2.x — a single,
monolithic `Polly.dll` with no `Polly.Core` dependency**. The shipped v0.1.8.2 zip's `Polly.dll`
is 287 KB (7.x), the publish output contains no `Polly.Core.dll` (not a transitive dep), and the
plugin loads **Active** live with Polly-wrapped HttpClients working (Jikan/AniList enrichment ran).
**Not a bug. No action.**

### ⚠️ REAL but server-mode only — "Postgres migration drift: Work.TitleTranslations has no column"
Confirmed: `MetaHub.Api/Program.cs:43` calls `db.Database.Migrate()`, and the EF migrations/snapshot
have no `title_translations` column (added to the model in v0.1.7.9 without a migration). A fresh
**PostgreSQL standalone server** would fail at runtime (42703). **But the embedded plugin — what all
Jellyfin users run — uses `EnsureCreated()` on SQLite and is unaffected.** So: real latent bug in the
optional standalone-server path, **not a plugin ship-blocker**. Fix when/if server mode is used:
`dotnet ef migrations add AddTitleTranslations` + regenerate snapshot + a Postgres round-trip test.

**Net: zero ship-blockers for the shipped (embedded) plugin.** The audit's "2 criticals are
ship-blockers" bottom line was overstated.

---

## Real, embedded-relevant bugs — recommended fix order

### High impact (affect the plugin everyone runs)
1. **Embedded SQLite concurrency — `GetMetadata` not exception-safe.** `MetaHubBackend.cs:42-67`
   (+ name-query 97-192, EnrichOnDemand 52-65). Parallel scan reads + on-demand writes to one SQLite
   file → "database is locked" propagates out of `GetMetadata`; Jellyfin silently drops that item's
   metadata/artwork. **Fix:** wrap embedded reads (return empty on DB error) + enable WAL +
   `busy_timeout` on the embedded connection + serialize on-demand enrichment per workId.
2. **Apply-to-library task aborts on the first failing item.** `Tasks/MetaHubApplyTask.cs:88-94`.
   Only `ResolveAsync` is guarded; a throw from `Compute`/`UpdateItemAsync` kills the whole walk.
   **Fix:** per-item try/catch (rethrow only `OperationCanceledException`), log Debug, continue.
3. **`OpenLibraryProvider.Parse` crashes on empty publisher/isbn_13 arrays.** `OpenLibraryProvider.cs:49-53`.
   `FirstOrDefault().GetString()` throws on an empty array (valid OL response) → book enrichment aborts.
   **Fix:** `ValueKind == JsonValueKind.String` guard, like the covers block.
4. **Fribb flattens TMDB tv/movie id namespaces → conflated works.** `Ingest/Anime/FribbDataset.cs:82-113`.
   `{"tv":N}`/`{"movie":N}` both become `Tmdb="N"`; overlapping id spaces collide, second dropped.
   **Fix:** preserve namespace (`tv:N` / `movie:N`) or split sources.
5. **Non-ASCII title match silently fails.** `MetaHubBackend.cs:129-150,196-204`. C# `ToLowerInvariant`
   (full Unicode) vs SQLite `lower()` (ASCII-only) never compare equal for umlaut/Cyrillic/full-width
   titles → name-fallback miss. **Fix:** store a precomputed folded-title column compared at write time.
6. **AniDB UDP client (opt-in, default off): session never re-authed on expiry; ban/fatal codes treated
   as "no match"; no retry/tag correlation.** `AniDb/AniDbUdpClient.cs`. After ~30 min all identification
   silently fails until restart; bans are sustained. **Fix:** reset+reauth on 501/506, circuit-break on
   555/600/601, tag+retry. (Lower urgency: AniDB is off by default.)

### Medium
7. **WorkMerger status: first provider's `Unknown` blocks a real status.** `WorkMerger.cs:45-48`.
8. **Person dedup case-sensitive in SQL, case-insensitive in memory → duplicate Person rows.** `WorkMerger.cs:88-103`.
9. **JikanEpisodeSync `ToDictionaryAsync` throws on duplicate episode → sync silently dies.** `JikanEpisodeSync.cs:64-66`.
10. **TMDB request omits `&language=` → always English text** (only matters with a TMDB key set). `TmdbProvider.cs:41-42`.
11. **Raw payloads + fetch-log discarded when no provider parses → TTL cache defeated, re-fetch risk.** `EnrichmentService.cs:83-103`.
12. **Polly backoff (2+4+8+16=30s) == HttpClient timeout (30s) → 429/slow become hard failures.** `Enrichment/DependencyInjection.cs:49-58`.
13. **ED2K hash wrong for files an exact multiple of 9,728,000 bytes (red/blue convention).** `Hashing/Ed2kHasher.cs:23-41`.
14. **manami entry spanning two pre-existing Works merges only one → other orphaned.** `AnimeIngestService.cs:58-67`.
15. **SequelKey prefix-only `\b{kw}` regex false positives** ("final"→"Final Fantasy"). `MetaHubBackend.cs:233-235`.

### Low (mostly correctness-hardening / UI polish)
- Single `SaveChangesAsync` for ~38k works (perf). `AnimeIngestService.cs`.
- Raw-payload table grows unboundedly. `EnrichmentService.cs:148-158`.
- No per-work concurrency guard in `EnrichAsync` (duplicate inserts under parallel runs).
- `Take(8)` before in-memory year/sequel filters → correct work can be truncated. `MetaHubBackend.cs:139-153`.
- Folder-title fallback gates on literal "Series", ignoring disabled-Anime opt-out. `MetaHubSeriesProvider.cs:52-56`.
- `HttpResponseMessage` not disposed when streaming a dataset. `HttpDatasetSource.cs:28-34`.
- Config page: search results ignore ancestor-disabled inheritance; parent toggle leaves child rows
  stale; primary-image URL requested even when no image (404s). `configPage.html`.

---

_Full raw agent report + per-finding verifier notes: workflow run `wf_06e06aff-ec5`._
