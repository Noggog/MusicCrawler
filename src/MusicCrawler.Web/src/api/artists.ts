import type { ArtistMetadata, CatalogSyncResult } from '../types'

// Calls the backend's GET /artists through the Vite dev proxy (/api -> backend).
// This now serves from the local catalog store, not live Plex; refresh it via
// refreshCatalog() below.
export async function getArtists(): Promise<ArtistMetadata[]> {
  const res = await fetch('/api/artists')
  if (!res.ok) {
    throw new Error(`Failed to load artists: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ArtistMetadata[]
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
