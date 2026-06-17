// Per-user discovery queue (the swipe loop). All calls require an authenticated session (cookie
// sent automatically, same-origin). artist goes in the query string so names with '/' work.
import type { DiscoveryCandidate, DiscoveryPage } from '../types'

export async function getQueue(page = 0, pageSize = 20): Promise<DiscoveryPage> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  const res = await fetch(`/api/discovery?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load discovery queue: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DiscoveryPage
}

// Rebuild the pending queue from the current seeds (keeps likes/dislikes). Use after editing seeds.
export async function refreshQueue(page = 0, pageSize = 20): Promise<DiscoveryPage> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  const res = await fetch(`/api/discovery/refresh?${params}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to rebuild discovery queue: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DiscoveryPage
}

export async function likeCandidate(artist: string): Promise<void> {
  const res = await fetch(`/api/discovery/like?${new URLSearchParams({ artist })}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to like ${artist}: ${res.status} ${res.statusText}`)
  }
}

export async function dislikeCandidate(artist: string): Promise<void> {
  const res = await fetch(`/api/discovery/dislike?${new URLSearchParams({ artist })}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to dislike ${artist}: ${res.status} ${res.statusText}`)
  }
}

export async function getPurchases(): Promise<DiscoveryCandidate[]> {
  const res = await fetch('/api/discovery/purchases')
  if (!res.ok) {
    throw new Error(`Failed to load wishlist: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DiscoveryCandidate[]
}
