import type { UnifiedRelations } from '../types'

// Calls the backend's GET /related?artist=... through the Vite dev proxy (/api -> backend).
// On a cache miss / stale entry the backend ingests from Deezer and persists the graph;
// pass refresh=true to force a re-fetch regardless of staleness. The artist goes in the query
// string (URLSearchParams encodes it) so names with '/' (e.g. "AC/DC") work.
export async function getRelated(artist: string, refresh = false): Promise<UnifiedRelations> {
  const params = new URLSearchParams({ artist })
  if (refresh) params.set('refresh', 'true')
  const res = await fetch(`/api/related?${params.toString()}`)
  if (!res.ok) {
    throw new Error(`Failed to load related artists: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as UnifiedRelations
}
