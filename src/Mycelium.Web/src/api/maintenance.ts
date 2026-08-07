// Maintenance sweeps for the library maintainer. All calls require an authenticated session.

// A combined-name entry (Plex's ';'-joined multi-artist names) the cleanup can resolve.
export interface CombinedNameEntry {
  scope: 'catalog' | 'artistRating' | 'albumRating'
  name: string
  album: string | null
  splitInto: string[]
  affected: number
}

// Counts of what a cleanup run changed.
export interface CleanupResult {
  catalogSplit: number
  artistRatingsSplit: number
  albumRatingsSplit: number
  pendingRemoved: number
}

// Preview the combined-name entries across the catalog and user ratings.
export async function getCombinedArtists(): Promise<CombinedNameEntry[]> {
  const res = await fetch('/api/maintenance/combined-artists')
  if (!res.ok) {
    throw new Error(`Failed to scan for combined names: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as CombinedNameEntry[]
}

// Resolve every combined-name entry: split catalog docs and re-attribute ratings.
export async function resolveCombinedArtists(): Promise<CleanupResult> {
  const res = await fetch('/api/maintenance/combined-artists/resolve', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Cleanup failed: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as CleanupResult
}
