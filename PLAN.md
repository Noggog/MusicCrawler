# MusicCrawler — Project Plan

> Living document. Captures the product vision, architecture, and phased build
> order. See `DEVELOPMENT.md` for how the current code is wired.

## Vision

A music-discovery tool that works like a **tree search** over artists:

1. Sync the artists (and their albums) that exist in a user's Plex library.
2. The user thumbs **up/down** library artists they already own. A thumbs-up is a
   *taste anchor* (what used to be a "seed"); thumbs-down means "not my taste".
3. The system recommends related artists, surfaced to thumb on.
4. **Thumbs down** = dead end (prune the branch).
   **Thumbs up** = (a) the artist's related artists join the recommendation pool
   (grow the branch), and (b) the artist is added to a **purchase list** to be
   acquired and added to Plex.

The frontier of "what to recommend next" continuously grows from approvals and
shrinks from rejections — always surfacing fresh artists rooted in the user's taste.

### Ratings replace seeds (2026-06-17)

There is no separate "seed" concept. Everything the user reacts to — owned
artists, recommended artists, and missing albums — is a **rating** (👍/👎). A
👍 on an *owned* artist is exactly what a seed was: a taste anchor the frontier
grows from. The Artists page is just the list of owned artists with thumbs (no
more star toggle), and a dedicated **Ratings** page lets the user review and
adjust every rating after the fact.

### Discovery feed = three toggleable categories (2026-06-17)

The Discover area surfaces three kinds of things to react to. Checkboxes pick
which categories are shown; each is its own paged section:

1. **Recommended artists** — new artists not in the library, grown from the
   user's 👍'd artists along the similarity graph (the original behaviour). 👍 =
   queue to buy + grow the frontier; 👎 = prune.
2. **Missing albums** — albums that exist on Deezer for an artist the user
   **already owns** but that aren't in the library. 👍 = queue the album to buy;
   👎 = not interested. Keeps owned bands current. A missing album drops out of
   the feed (and out of Ratings) automatically once it appears in the library.
3. **Unrated owned artists** — library artists the user hasn't thumbed yet. This
   is the alternative to seed-starring: thumbing owned bands feeds the
   recommendation frontier. Computed as *catalog minus already-rated*.

## Core architectural principle: self-sufficient sections

Each subsystem owns a **local database store as its source of truth for daily
operations**. External services (Plex, Deezer, downloader) are touched only by
**sync jobs**, never on the hot path. The app stays fully usable — browse, seed,
swipe, review the purchase list — even when Plex or Deezer are offline.

External services are *refreshable inputs*, not runtime dependencies.

## Decisions (locked)

- **Similarity source: Deezer** (keyless `/search/artist` → `/artist/{id}/related`,
  also backfills artist images). Replaces the deprecated Spotify recommendations
  API (403s since 2024-11-27). Lives behind the existing `IRecommendationProvider`.
- **Identity: Authentik (OIDC).** Core login does *not* depend on Plex. Light
  multi-user for trusted friends — functional, not paranoid.
- **Plex is a single shared server — THE library**, host-configured (as today via
  env: `plexEndpoint` / `PLEX_TOKEN` / `preferredPlexLibrary`). The Library Catalog
  is one global store, not per-user. Authentik still scopes per-user *taste* state
  (seeds/decisions/purchase); per-user Plex linking, if ever added, is for other
  purposes, not the catalog.
- **Recommendations are precomputed into the DB**, not computed on page visit. A
  per-user recommendation queue is materialized and kept fresh **incrementally on
  each decision** (reactive tree-search) **plus a periodic replenisher** (background
  reconcile). Site visits are always an instant DB read of "the next card."
- **Review UX: one-at-a-time swipe**, showing *why* an artist was recommended
  (which seeds/approved artists point to it).
- **Purchase list is its own store.** It tracks what to grab with a status field.
  The actual downloader integration is a future pluggable sync job behind an
  interface — target (e.g. Lidarr) decided later.

## Sections

Each is independently buildable and testable.

### 1. Library Catalog
- **Store:** `artists` — name, image, lastSeenAt, **owned `albums`** (album
  titles pulled from Plex). **Global / shared** — one Plex server, THE library.
- **Sync job:** `CatalogRefresher` ("Refresh from Plex") — upserts artists *and*
  their owned albums on startup / daily; flags artists no longer present.
- **Daily reads** (`GET /artists`, the missing-album diff) hit this store, not Plex.
- _Built:_ DB-backed `LibraryProvider` over `ArtistCatalogRepo`; the album column
  and `PlexApi.GetAlbums`/`PlexRepo.QueryAllAlbums` are the 2026-06-17 addition.

### 1b. Missing Albums (global)
- **Store:** `missingAlbums` — one doc per (owned artist, album-on-Deezer-not-owned),
  with album art. **Global / shared** (a fact about the library, not a user).
- **Sync job:** `MissingAlbumRefresher` / `AlbumSyncService` — for each owned
  artist, resolve its Deezer id, pull its discography (`record_type == "album"`),
  diff against the owned album titles, and `ReplaceForArtist` the misses. Albums
  that have since been acquired drop out on the next run.
- Heavy (one Deezer discography call per owned artist), so it is its own daily job
  separate from the cheap Plex catalog refresh.

### 2. Similarity Graph (recommendation ingestion)
- **Store:** `relatedArtists` — edges `artist → [related]`, tagged with source
  (`deezer`), fetchedAt, and related-artist images. **Global / shared** across users.
- **Sync job:** Deezer provider; results **persisted** so the graph survives
  Deezer downtime and we never re-fetch the same artist needlessly.
- _Current code:_ `SpotifyProvider` (dead). Add `DeezerProvider` behind
  `IRecommendationProvider`; register in `MainModule`.

### 3. User Taste State (per user)
- **Stores (scoped by Authentik user id):**
  - `userQueue` — per (user, artist) ratings *and* the precomputed recommendation
    queue. Status Pending (recommended, awaiting a swipe) / Liked / Disliked. A
    Liked artist is a taste anchor (the old "seed"); score/sources/depth rank the
    pending recommended ones.
  - `userAlbumRatings` — per (user, artist, album) verdict on a missing album.
- **No `seeds` store** — removed 2026-06-17. The frontier = the user's Liked
  artists (owned or recommended-then-liked). Bootstrapping: a brand-new user
  thumbs owned artists (feed category 3), which seeds the frontier.
- _Built:_ `IUserQueueRepo`/`UserQueueRepo`; `IUserAlbumRatingRepo` is the
  2026-06-17 addition. `IUserSeedRepo` deleted.

### 4. Tree-Search Engine
- **Store:** per-user `recommendationQueue` — the materialized, ranked list of
  pending cards. The swipe UI only ever reads from here (instant, offline-from-sources).
- Queue computation (per user):
  - frontier = the user's **Liked artists** (owned taste anchors + approved recs)
  - expand via the stored similarity graph
  - exclude already-in-library, rejected (dead ends), already-decided
  - rank by how many frontier artists point to a candidate (more = stronger)
- **Kept fresh two ways:**
  - **Incremental, on each decision** (reactive): thumbs-up → mark approved → if the
    artist's edges aren't in the graph, enqueue a Deezer fetch → splice its related
    artists into the queue with updated ranking. Thumbs-down → mark rejected, drop
    from queue. The next card already reflects the last swipe.
  - **Periodic replenisher** (background job): fetch missing Deezer edges, recompute
    rankings, top up and prune the queue. Reconciles anything the incremental step
    deferred (e.g. Deezer offline during a swipe).
- First-ever seeding shows a brief "building recommendations" state, then the queue
  stays pre-warmed.

### 5. Acquisition / Purchase List (global) — _Built 2026-06-17_
- **Store:** `purchases` — one doc per item (artist or missing album), status
  `pending → sent → in-library`. **Global / unified across users** (the maintainer's
  queue), keyed by `PurchaseKey` (`artist:{name}` / `album:{artist} {album}`).
  `IPurchaseRepo`/`PurchaseRepo`; display fields refresh on upsert, status/requestedAt
  are insert-only so a reconcile never demotes an ordered row.
- **`PurchaseService`** (Backend singleton) owns the lifecycle. `Reconcile()` is the one
  sync point: folds the current liked-but-unowned set in (insert as pending / dedup
  across users), flips arrivals to `in-library`, and prunes pending rows nobody wants
  any more (ordered rows are kept — in flight). Runs on each read of the list and after
  each catalog/album sync, so the loop closes without a page visit.
- **Integration job (stubbed):** `IDownloader` is the pluggable seam; `NoOpDownloader`
  logs + accepts, so "Order" advances an item to `sent` today. Real target (Lidarr?)
  drops in later without touching the list or UI.
- **Endpoints:** `GET /purchases` (active = pending + sent), `POST /purchases/order`,
  `POST /purchases/unsend`, `DELETE /purchases` (all by `?id=`). Frontend Purchases.tsx
  splits Pending / Ordered with Order / Undo / ✕ actions.

### 6. Web UI (React + Vite)
- **Artists** — owned-artist list with 👍/👎 per row (replaces the seed star).
- **Discover** — three category-checkbox sections (recommended artists, missing
  albums, unrated owned artists); list ⇄ swipe; "why recommended".
- **Ratings** — review/adjust every rating (artists + albums); albums that now
  exist are hidden (no longer interesting). Re-thumb or clear back to the feed.
- **To Buy** — purchase list: non-owned Liked artists + still-missing Liked albums.
- _Built:_ Home, Artists, Discover, To Buy, dev Related view.

## Phased build order

Each phase is shippable on its own.

1. **Catalog + Plex refresh job** — convert `/artists` to DB-backed; add the sync.
   The foundation everything reads from.
2. **Deezer provider + similarity graph store** — replace dead Spotify; persist edges.
3. **Authentik OIDC login + per-user seeds** — identity, then mark library artists as liked.
4. **Tree-search engine + swipe UI** — the core discovery loop.
5. **Purchase list store + status tracking** _(built 2026-06-17)_ — persisted `purchases`
   store, `pending → sent → in-library` lifecycle reconciled from likes + library state,
   downloader behind a stubbed `IDownloader` interface.
6. **Richer discovery feed (2026-06-17, in progress)** — seeds→ratings unification;
   three toggleable feed categories (recommended artists, missing albums, unrated
   owned artists); the album sync pipeline (Plex albums + Deezer discography diff →
   `missingAlbums`); and a Ratings review page. Artists page gets 👍/👎.

## Open questions

- **Downloader target** (Phase 5): TBD; the interface keeps it swappable.
- **Graph refresh policy:** how stale before re-fetching a Deezer artist's related list.
- **Replenisher cadence:** how often the periodic queue job runs, and whether
  decisions trigger it (debounced) in addition to the schedule.
