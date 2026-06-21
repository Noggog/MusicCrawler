import type { ArtistSources, SourceCandidate, SourceIdentity } from '../types'

// Cross-source identity API for the Artists-page "Sources" tab. One generic set of routes over
// every backend ISourceIdentityCorrector (deezer, musicbrainz, …), dispatched by the source key.
// All are auth-gated server-side (maintainer corrections).

// Every source's resolved identity (id + link + override flag) for one artist.
export async function getArtistSources(artist: string): Promise<ArtistSources> {
  const params = new URLSearchParams({ artist })
  const res = await fetch(`/api/artists/sources?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to load sources for ${artist}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ArtistSources
}

// Free-text candidate search within one source, for that source's correction picker.
export async function searchSource(source: string, q: string): Promise<SourceCandidate[]> {
  const params = new URLSearchParams({ q })
  const res = await fetch(`/api/sources/${source}/search?${params}`)
  if (!res.ok) {
    throw new Error(`Failed to search ${source}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as SourceCandidate[]
}

// Pin an artist to a specific id on one source (sticky override). The backend re-derives that
// artist's similarity edges and rebuilds the queue. Returns the pinned identity.
export async function pinSource(source: string, artist: string, id: string): Promise<SourceIdentity> {
  const params = new URLSearchParams({ artist, id })
  const res = await fetch(`/api/artists/sources/${source}?${params}`, { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to pin ${source} for ${artist}: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as SourceIdentity
}

// Clear a source's pin so the artist re-resolves from a name search next time.
export async function clearSource(source: string, artist: string): Promise<void> {
  const params = new URLSearchParams({ artist })
  const res = await fetch(`/api/artists/sources/${source}?${params}`, { method: 'DELETE' })
  if (!res.ok) {
    throw new Error(`Failed to clear ${source} for ${artist}: ${res.status} ${res.statusText}`)
  }
}
