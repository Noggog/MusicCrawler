import type { ArtistListItem, CatalogSyncResult, DeezerCandidate } from '../types'

// Calls the backend's GET /artists through the Vite dev proxy (/api -> backend).
// This now serves from the local catalog store, not live Plex; refresh it via
// refreshCatalog() below. Each row carries the artist's resolved Deezer identity.
export async function getArtists(): Promise<ArtistListItem[]> {
  const res = await fetch('/api/artists')
  if (!res.ok) {
    throw new Error(`Failed to load artists: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ArtistListItem[]
}

// Triggers the Library Catalog sync job (POST /catalog/refresh): the backend pulls
// the artist list from Plex and upserts the catalog. The only Plex-touching call.
export async function refreshCatalog(): Promise<CatalogSyncResult> {
  const res = await fetch('/api/catalog/refresh', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to refresh catalog: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as CatalogSyncResult
}

// Pin a library artist to a specific Deezer artist id (fix a misassociation). The backend stores
// a sticky override and re-ingests that artist's similarity edges. Returns the pinned identity.
export async function setDeezerId(artist: string, id: number): Promise<DeezerCandidate> {
  const params = new URLSearchParams({ artist, id: String(id) })
  const res = await fetch(`/api/artists/deezer-id?${params}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to set Deezer id for ${artist}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DeezerCandidate
}

// Clear a Deezer override so the artist re-resolves from a name search next time.
export async function clearDeezerId(artist: string): Promise<void> {
  const params = new URLSearchParams({ artist })
  const res = await fetch(`/api/artists/deezer-id?${params}`, { method: 'DELETE' })
  if (!res.ok) {
    throw new Error(`Failed to clear Deezer id for ${artist}: ${res.status} ${res.statusText}`)
  }
}

// Dev-only one-shot: resolve + persist the Deezer identity for every library artist so the page
// can flag misassociations. Returns how many of the total resolved.
export async function resolveAllDeezer(): Promise<{ total: number; resolved: number }> {
  const res = await fetch('/api/artists/deezer/resolve-all', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to resolve Deezer for all: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as { total: number; resolved: number }
}
