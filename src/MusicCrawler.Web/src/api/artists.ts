import type { ArtistMetadata } from '../types'

// Calls the backend's GET /artists through the Vite dev proxy (/api -> backend).
// Replaces the old C# ArtistApiClient. Note: the backend never implemented the
// /artistsSearch endpoint the old client referenced, so search is intentionally omitted.
export async function getArtists(): Promise<ArtistMetadata[]> {
  const res = await fetch('/api/artists')
  if (!res.ok) {
    throw new Error(`Failed to load artists: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ArtistMetadata[]
}
