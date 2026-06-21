import type { ArtistLibraries } from '../types'

// Per-library presence + deep links for the Artists-page "Library" tab (Plex now, Navidrome later).
// Auth-gated server-side, like the cross-source identity routes.
export async function getArtistLibraries(artist: string): Promise<ArtistLibraries> {
  const params = new URLSearchParams({ artist })
  const res = await fetch(`/api/artists/libraries?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load libraries for ${artist}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ArtistLibraries
}
