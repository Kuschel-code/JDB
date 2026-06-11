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
- **Kitsu** 🆓 — JSON:API.
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
