// Per-user discovery feed + ratings. All calls require an authenticated session (cookie sent
// automatically, same-origin). artist/album go in the query string so names with '/' work.
import type {
  ArtistAlbumItem,
  DiscoveryFeedPage,
  DownloadSnapshot,
  FeedItem,
  FeedKind,
  PurchaseItem,
  RatedItem,
} from '../types'

export type Verdict = 'up' | 'down'

// How long a snoozed recommendation stays hidden before it resurfaces in the feed.
export type SnoozeDuration = 'week' | 'month' | 'year'

// A paged feed section for one category (recommended artists, missing albums, unrated owned artists).
export async function getFeed(kind: FeedKind, page = 0, pageSize = 20): Promise<DiscoveryFeedPage> {
  const params = new URLSearchParams({ kind, page: String(page), pageSize: String(pageSize) })
  const res = await fetch(`/api/discovery?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load ${kind} feed: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DiscoveryFeedPage
}

// A single mixed feed across the selected categories, round-robin interleaved + shuffled by `seed`
// (same seed → same order across pages). Each item carries its own `kind`.
export async function getMixedFeed(
  kinds: FeedKind[],
  page = 0,
  pageSize = 20,
  seed = 0,
): Promise<DiscoveryFeedPage> {
  const params = new URLSearchParams({
    kinds: kinds.join(','),
    page: String(page),
    pageSize: String(pageSize),
    seed: String(seed),
  })
  const res = await fetch(`/api/discovery/mixed?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load discovery feed: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DiscoveryFeedPage
}

// A liked non-owned artist's acquirable albums (their Deezer discography minus anything owned),
// surfaced inline under the just-rated card. Fetched on demand only when an artist is liked.
export async function getArtistAlbums(artist: string): Promise<FeedItem[]> {
  const params = new URLSearchParams({ artist })
  const res = await fetch(`/api/discovery/artist-albums?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load albums for ${artist}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as FeedItem[]
}

// An owned artist's full discography (owned + missing albums, each flagged) for the Artists-page
// drill-down. One Deezer call server-side; missing albums carry the user's verdict if already rated.
export async function getArtistDiscography(artist: string): Promise<ArtistAlbumItem[]> {
  const params = new URLSearchParams({ artist })
  const res = await fetch(`/api/discovery/artist-discography?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load discography for ${artist}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ArtistAlbumItem[]
}

// Dev-only: rebuild the pending recommendations for every user from their liked artists (keeps
// ratings). Returns the number of users rebuilt.
export async function refreshQueue(): Promise<{ rebuilt: number }> {
  const res = await fetch('/api/discovery/refresh', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to rebuild recommendations: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as { rebuilt: number }
}

// Thumb an artist or — when album is supplied — a missing album.
export async function rate(item: FeedItem | RatedItem, verdict: Verdict): Promise<void> {
  const params = new URLSearchParams({ artist: item.artist.artistName, verdict })
  if (item.album) {
    params.set('album', item.album)
    if (item.imageUrl) params.set('albumArt', item.imageUrl)
  }
  const res = await fetch(`/api/discovery/rate?${params}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to rate ${item.artist.artistName}: ${res.status} ${res.statusText}`)
  }
}

// Ad-hoc seed: add an artist that's not in the library and that nothing recommends yet, by pinning a
// chosen source candidate (by id) and liking it — it then grows the frontier and joins the buy list.
// Used by the Artists "Search all of Deezer" results, where each hit carries its source id.
export async function seedArtist(source: string, id: string, artist: string): Promise<void> {
  const params = new URLSearchParams({ source, id, artist })
  const res = await fetch(`/api/discovery/seed?${params}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to add ${artist}: ${res.status} ${res.statusText}`)
  }
}

// Snooze an artist or — when album is supplied — a missing album (hidden for the chosen duration;
// resurfaces when the window lapses).
export async function snooze(item: FeedItem | RatedItem, duration: SnoozeDuration): Promise<void> {
  const params = new URLSearchParams({ artist: item.artist.artistName, duration })
  if (item.album) {
    params.set('album', item.album)
    if (item.imageUrl) params.set('albumArt', item.imageUrl)
  }
  const res = await fetch(`/api/discovery/snooze?${params}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to snooze ${item.artist.artistName}: ${res.status} ${res.statusText}`)
  }
}

// Clear a rating, returning the artist/album to the feed.
export async function clearRating(item: FeedItem | RatedItem): Promise<void> {
  const params = new URLSearchParams({ artist: item.artist.artistName })
  if (item.album) params.set('album', item.album)
  const res = await fetch(`/api/discovery/rate?${params}`, { method: 'DELETE' })
  if (!res.ok) {
    throw new Error(`Failed to clear rating for ${item.artist.artistName}: ${res.status} ${res.statusText}`)
  }
}

// Every rating the user has made, for the review page (albums that now exist are filtered out).
export async function getRatings(): Promise<RatedItem[]> {
  const res = await fetch('/api/discovery/ratings')
  if (!res.ok) {
    throw new Error(`Failed to load ratings: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as RatedItem[]
}

// The shared "to buy" list: liked non-owned artists + liked albums not yet acquired, with a
// persisted status (pending → sent → in-library). In-library items have dropped off.
export async function getPurchases(): Promise<PurchaseItem[]> {
  const res = await fetch('/api/purchases')
  if (!res.ok) {
    throw new Error(`Failed to load wishlist: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as PurchaseItem[]
}

// A live snapshot of the download subsystem for the monitor panel (polled).
export async function getDownloadStatus(): Promise<DownloadSnapshot> {
  const res = await fetch('/api/purchases/status')
  if (!res.ok) {
    throw new Error(`Failed to load download status: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DownloadSnapshot
}

// Manually queue an item for download now (non-blocking — the drainer does the fetch). Also the
// "retry" action for failed items. Works whether or not automatic downloads are on.
export async function downloadPurchase(id: string): Promise<void> {
  const res = await fetch(`/api/purchases/download?id=${encodeURIComponent(id)}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to queue download: ${res.status} ${res.statusText}`)
  }
}

// Undo — move a downloaded/queued item back to "pending".
export async function unsendPurchase(id: string): Promise<void> {
  const res = await fetch(`/api/purchases/unsend?id=${encodeURIComponent(id)}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to revert item: ${res.status} ${res.statusText}`)
  }
}

