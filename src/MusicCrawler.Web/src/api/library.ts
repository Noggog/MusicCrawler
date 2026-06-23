import type { ArtistLibraries, ArtistRatingStats } from '../types'

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

// The user's per-song Plex rating summary (highest / lowest / average, 0–5 stars) for one artist, for
// the detail readout. Auth-gated server-side. Returns present=false for artists not in Plex.
export async function getArtistRatings(artist: string): Promise<ArtistRatingStats> {
  const params = new URLSearchParams({ artist })
  const res = await fetch(`/api/artists/ratings?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load ratings for ${artist}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ArtistRatingStats
}
