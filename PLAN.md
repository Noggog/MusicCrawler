# MusicCrawler — Project Plan

> Living document. Captures the product vision, architecture, and phased build
> order. See `DEVELOPMENT.md` for how the current code is wired.

## Vision

A music-discovery tool that works like a **tree search** over artists:

1. Sync the artists that exist in a user's Plex library.
2. The user marks library artists they **like** → these are *seeds*.
3. The system recommends related artists, surfaced **one at a time** to swipe on.
4. **Thumbs down** = dead end (prune the branch).
   **Thumbs up** = (a) the artist's related artists join the recommendation pool
   (grow the branch), and (b) the artist is added to a **purchase list** to be
   acquired and added to Plex.

The frontier of "what to recommend next" continuously grows from approvals and
shrinks from rejections — always surfacing fresh artists rooted in the user's taste.

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
- **Store:** `artists` — name, image, Plex key, lastSeenAt. **Global / shared** —
  one Plex server, THE library.
- **Sync job:** "Refresh from Plex" — upserts the catalog on demand / schedule;
  flags artists no longer present.
- **Daily reads** (`GET /artists`, seed-picking) hit this store, not Plex.
- _Current code:_ `GET /artists` hits Plex live with a 30s Redis cache
  (`LibraryProvider` / `PlexRepo`). Convert to DB-backed + a sync job.

### 2. Similarity Graph (recommendation ingestion)
- **Store:** `relatedArtists` — edges `artist → [related]`, tagged with source
  (`deezer`), fetchedAt, and related-artist images. **Global / shared** across users.
- **Sync job:** Deezer provider; results **persisted** so the graph survives
  Deezer downtime and we never re-fetch the same artist needlessly.
- _Current code:_ `SpotifyProvider` (dead). Add `DeezerProvider` behind
  `IRecommendationProvider`; register in `MainModule`.

### 3. User Taste State (per user)
- **Store:** `seeds` (liked library artists) and `decisions` (thumbs up/down per
  recommended artist), scoped by Authentik user id.
- _Current code:_ none. `RecommendationInteractor` currently hardcodes "first 10
  library artists" as seeds — replace with real seeds.

### 4. Tree-Search Engine
- **Store:** per-user `recommendationQueue` — the materialized, ranked list of
  pending cards. The swipe UI only ever reads from here (instant, offline-from-sources).
- Queue computation (per user):
  - frontier = seeds + approved artists
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

### 5. Acquisition / Purchase List (per user)
- **Store:** approved artists queued for purchase, status `pending → sent → in-library`.
- **Integration job (future):** push `pending` items to a downloader behind an
  interface; the Library refresh closes the loop (artist appears in Plex → status
  becomes `in-library` and it drops off the list).

### 6. Web UI (React + Vite)
- Seed-picking over the catalog.
- One-at-a-time swipe review (with "why recommended").
- Purchase list view.
- Settings: connect Plex account/token.
- _Current code:_ Home + a read-only Artists table.

## Phased build order

Each phase is shippable on its own.

1. **Catalog + Plex refresh job** — convert `/artists` to DB-backed; add the sync.
   The foundation everything reads from.
2. **Deezer provider + similarity graph store** — replace dead Spotify; persist edges.
3. **Authentik OIDC login + per-user seeds** — identity, then mark library artists as liked.
4. **Tree-search engine + swipe UI** — the core discovery loop.
5. **Purchase list store + status tracking** — downloader push wired later behind an interface.

## Open questions

- **Downloader target** (Phase 5): TBD; the interface keeps it swappable.
- **Graph refresh policy:** how stale before re-fetching a Deezer artist's related list.
- **Replenisher cadence:** how often the periodic queue job runs, and whether
  decisions trigger it (debounced) in addition to the schedule.
