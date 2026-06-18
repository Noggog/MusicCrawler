// Per-user discovery feed + ratings. All calls require an authenticated session (cookie sent
// automatically, same-origin). artist/album go in the query string so names with '/' work.
import type { DiscoveryFeedPage, FeedItem, FeedKind, PurchaseItem, RatedItem } from '../types'

export type Verdict = 'up' | 'down'

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

// Rebuild the pending recommendations from the current liked artists (keeps ratings).
export async function refreshQueue(): Promise<void> {
  const res = await fetch('/api/discovery/refresh', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to rebuild recommendations: ${res.status} ${res.statusText}`)
  }
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

// Order an item — hand it to the downloader and advance it to "sent".
export async function orderPurchase(id: string): Promise<void> {
  const res = await fetch(`/api/purchases/order?id=${encodeURIComponent(id)}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to order item: ${res.status} ${res.statusText}`)
  }
}

// Undo an order, moving the item back to "pending".
export async function unsendPurchase(id: string): Promise<void> {
  const res = await fetch(`/api/purchases/unsend?id=${encodeURIComponent(id)}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to revert item: ${res.status} ${res.statusText}`)
  }
}

// Re-queue a failed item for another download attempt.
export async function retryPurchase(id: string): Promise<void> {
  const res = await fetch(`/api/purchases/retry?id=${encodeURIComponent(id)}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to retry item: ${res.status} ${res.statusText}`)
  }
}

// Remove an item from the list entirely.
export async function removePurchase(id: string): Promise<void> {
  const res = await fetch(`/api/purchases?id=${encodeURIComponent(id)}`, { method: 'DELETE' })
  if (!res.ok) {
    throw new Error(`Failed to remove item: ${res.status} ${res.statusText}`)
  }
}
