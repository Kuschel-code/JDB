# Data sources & datasets

Curated catalogue of metadata providers and ID-mapping datasets MetaHub can use.
The first block per media type repeats the concept's primary sources; the
**"Additional"** blocks are extra databases worth integrating, gathered while
researching the ecosystem.

Rule of thumb: prefer **official APIs over HTML scraping** — more stable, ToS-friendly,
less maintenance. Always send a descriptive `User-Agent` with contact info and respect
rate limits.

Legend: 🔑 needs API key · 🆓 free / no key · ⚠️ strict rate limits / registration

---

## Music

Primary:
- **MusicBrainz** ⚠️ — backbone; stable MBIDs, relationships. ~1 req/s, User-Agent required.
- **Cover Art Archive** 🆓 — cover art keyed to MusicBrainz release IDs.
- **Discogs** 🔑 — release/label depth, marketplace data.
- **TheAudioDB** 🔑 — artist images, biographies, genres.
- **Last.fm** 🔑 — tags, listening stats, similar artists.

Additional:
- **ListenBrainz** 🆓 — listening data, recommendations, popularity signals (MetaBrainz).
- **AcousticBrainz** 🆓 — acoustic/feature analysis (archived but still downloadable).
- **Deezer API** 🆓 — previews, album/track metadata, genre tree (no key for public data).
- **Spotify Web API** 🔑 — popularity, audio features, canonical artist/album art.
- **Genius** 🔑 — lyrics metadata, song credits.
- **Fanart.tv (music)** 🔑 — high-quality artist backgrounds, logos, CD art.

## Movies & Series

Primary:
- **TMDB** 🔑🆓 — main source: plot, cast/crew, posters, backdrops, episodes.
- **TheTVDB** 🔑 — strong series/episode data, ordering schemes.
- **OMDb** 🔑 / **IMDb datasets** 🆓 — ratings, extra fields.
- **Trakt** 🔑 — lists, popularity, watch progress.

Additional:
- **Fanart.tv (movies/tv)** 🔑 — clearlogos, clearart, posters, thumbs, banners.
- **Simkl API** 🔑 — unified movies/TV/anime catalogue + tracking.
- **OpenSubtitles** 🔑 — MovieHash (size+chunk) as an extra identification signal; subtitles.
- **JustWatch** (unofficial) ⚠️ — availability/streaming offers (metadata only, no streams).
- **Wikidata / DBpedia** 🆓 — cross-domain bridge, awards, franchises.
- **TVmaze** 🆓 — episode schedules, air dates, network info.

## Anime

Primary:
- **AniList** 🆓 — GraphQL, generous limits.
- **Jikan** 🆓 — unofficial MAL API (scores, rankings).
- **Kitsu** 🆓 — JSON:API. ⚠️ *Watch:* platform development has stalled (paid Pro tier
  removed from the codebase in 2026); the API itself is still up (`kitsu.app/api/edge`,
  ~99.8% uptime as of mid-2026) but unmaintained — no active roadmap. Fine to keep as a
  low-priority fallback provider; don't expect new features or a fast fix if it breaks.
- **AniDB** ⚠️ — very comprehensive; strict ToS, hard limits, client registration. Use defensively, cache hard.

Additional:
- **AnimeThemes.moe** 🆓 — opening/ending themes, audio/video.
- **AnimeNewsNetwork (Encyclopedia API)** 🆓 — staff, companies, detailed credits.
- **TheTVDB / TMDB (via Fribb mapping)** — artwork + episode data Jellyfin expects.
- **anisearch.com**, **LiveChart.me**, **Notify.moe**, **anime-planet.com** — already
  carried as cross-IDs by manami; useful as additional enrichment targets.
- **Shikimori** ⚠️ — Russian/EN anime/manga DB with rich relations.

Japanese sources (native titles, Japanese episode data):
- **Annict** 🔑 — Japanese anime database/tracker (annict.com); GraphQL API with personal
  access token. **(Integrated: AnnictProvider, linked via the ARM mapping.)**
- **Syoboi Calendar** 🆓 — Japanese TV anime schedule DB (cal.syoboi.jp); TIDs are ingested
  via ARM, episode endpoint is a future enrichment candidate.
- **MediaArts DB (メディア芸術データベース)** 🆓 — Japanese government anime/manga database.
- **kawaiioverflow/arm** 🆓 — mapping dataset MAL/AniList ↔ Annict/Syobocal. **(Used in ingest.)**
- **metachan-api** 🆓 — community project unifying anime & manga metadata (self-hostable).

## Regional direct connections (China, Korea, Japan)

Researched on request: MetaHub's anime/movie sources skew EN/JA. These are official,
non-scraping options for China- and Korea-native metadata (Japan is already well covered
above via Annict/Syoboi/MediaArts DB).

**Deprioritized: China.** Chinese-sourced metadata (Bangumi included) is subject to PRC
content moderation — Douban is documented removing award mentions and LGBTQ-themed
listings under political pressure — a real accuracy/consistency risk for a project whose
job is to be a canonical source, not just an additional-language nice-to-have. Kept below
for the record, but Japan-depth and the translation question (next section) are the
priority instead.

- **Bangumi (bgm.tv)** 🔑⚠️ — China's largest ACG (anime/comic/game) database. Official
  public API at `api.bgm.tv/v0/`, open-source server (Go, [bangumi/server] on GitHub).
  OAuth2 (authorization-code grant, 7-day access tokens + refresh) or a simpler personal
  access token for read-mostly use. Would fill a real content gap — Chinese-language
  titles/synopses and *donghua* (Chinese animation) coverage — but see the moderation
  caveat above; no confirmed commercial-use terms found either. Not pursued for now.
- **Naver Open API (Korea)** 🔑 — Naver (Korea's dominant search engine) exposes dedicated
  Movie, Book, and Encyclopedia search endpoints (`openapi.naver.com/v1/search/...`),
  simple `X-Naver-Client-Id`/`X-Naver-Client-Secret` header auth (no OAuth flow), 25,000
  calls/day free quota, commercial use explicitly allowed with attribution. Good fit for
  Korean movies/dramas and Korean-language book titles — the lowest-friction of the three
  Korean options to integrate.
- **KMDb (한국영상자료원)** 🔑 — the Korean Film Archive's (government, Ministry of Culture)
  official movie database; Open API exists (kmdb.or.kr/info/api). More authoritative for
  pure Korean film data than Naver, but requires a site membership account — more signup
  friction for comparable value, so prefer Naver unless KMDb-specific depth is needed.
- **Aladin Open API (Korea)** 🔑 — major Korean online bookstore; book search, ISBN lookup,
  bestseller lists via a TTB key. Candidate for Korean book coverage alongside Open
  Library/Google Books, same role the DNB (German) entry plays below.
- **National Diet Library Search API (Japan)** 🆓 — official Japanese National Diet Library;
  ISBN/bibliographic search, no key needed. ⚠️ The Book Cover API portion is being shut
  down 2026-03-31 (JPRO licensing change) — bibliographic text data only, no cover images
  from this source going forward.

Investigated and **ruled out**:
- **Douban (China)** — no viable official API; the developer API has been dead for years
  and every current "Douban API" offering found is an HTML scraper. Contradicts this
  project's official-APIs-only rule; don't revisit unless Douban ships a real API again.
- **Bilibili / Tencent Video / iQiyi donghua metadata** — these platforms host most
  donghua but publish no public metadata API; only option would be scraping. Bangumi
  covers the same content gap legitimately instead.
- **Filmarks / Eiga.com (Japan)** — Japan's largest movie/anime review apps (Filmarks:
  ~120k movies, ~20k dramas, ~6k anime cataloged; Eiga.com founded 1998, one of Japan's
  biggest film info sites). Neither publishes a public API — no developer portal found for
  either. Would have been a strong native-Japanese review/rating source; scraping-only,
  so not viable under this project's rule.

[bangumi/server]: https://github.com/bangumi/server

## Translation coverage (the "why is my anime overview in English" question)

No currently integrated anime provider supplies overview text outside en/ja/ru — confirmed
by reading every provider's `OverviewTranslations` usage: AniList and Kitsu only carry
en/ja titles (no translated overview at all), Jikan is English-only, Annict is Japanese-only,
Shikimori is the one anime source with a translated overview, and it's Russian
(`data.OverviewTranslations["ru"]`). TMDB is the only source that can supply a genuinely
localized (e.g. German) overview, via its `language=` query param — but that only reaches
anime that has a Fribb-derived TMDB cross-id *and* a community-contributed translation
there. For most anime, a non-English/non-Japanese `PreferredLanguage` user gets an English
overview and no more.

Researched as a possible fix — a machine-translation fallback (translate the
highest-priority available overview into `PreferredLanguage` only when no native
translation exists, cached hard so it runs once per work):

- **DeepL API** — best translation quality of the three, but **API Free was discontinued
  for new customers in July 2026**; new integrations only get paid Developer/Growth plans.
  No longer a "free tier" option.
- **Google Cloud Translation API** — genuine free tier (500k characters/month, recurring,
  permanent, good NMT quality) — but requires a GCP account with billing details on file
  even though usage stays free, which is friction beyond MetaHub's "no account, no cloud
  dependency" defaults for most providers.
- **LibreTranslate** — zero cost, zero account/ToS friction, but runs as its own service
  (Docker container) — contradicts the embedded mode's "no Docker, no server" pitch for
  most users — and quality is noticeably weaker (more grammar errors, occasional meaning
  shifts) than DeepL/Google.

**Decision: not implemented.** Every option adds either a cost/billing dependency or an
extra self-hosted service, for a feature that only affects the overview text of anime
without an English/Japanese/Russian description already covering it. English remains the
fallback. Revisit if DeepL reinstates a free tier, or if a user wants to self-host
LibreTranslate and have MetaHub call it optionally.

## Books

Primary:
- **Open Library** 🆓 — free API, ISBN lookups, covers.
- **Google Books** 🔑🆓 — broad coverage, descriptions.
- **ISBNdb** 🔑 — edition depth (paid).

Additional:
- **Hardcover API** 🔑 — modern GraphQL book DB (Goodreads-style), series data.
- **WorldCat / OCLC** ⚠️ — authoritative library catalogue (registration).
- **Deutsche Nationalbibliothek (DNB) SRU** 🆓 — strong German-language coverage.
- **Comic Vine** 🔑 — comics/manga issues, volumes, characters.
- **MangaUpdates / MangaDex API** 🆓 — manga series, chapters, scanlation metadata.
- **LibraryThing / Bookwyrm** — community tags, series, recommendations.

## ID-mapping datasets (don't build cross-linking yourself)

- **manami-project/anime-offline-database** — ~35k anime with cross-refs to MAL, AniDB,
  AniList, Kitsu, etc. Weekly releases. License: ODbL/AGPL. **(Used in M2.)**
- **Fribb/anime-lists** — adds TVDB/TMDB/IMDb ids merged on the AniDB id. **(Used in M2.)**
- **Wikidata** — universal QID bridge across TMDB/MusicBrainz/ISBN/AniList and media types.
- **MusicBrainz / Discogs dumps** — bulk downloads for offline music ingest.
- **IMDb datasets** — official TSV dumps (titles, ratings, principals).

---

## Universal bridge

**Wikidata** links entities across all the domains above via a single QID, making it the
ideal fallback connector when a direct cross-ID is missing.
