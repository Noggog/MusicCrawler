// TS mirrors of the C# contracts in MusicCrawler.Interfaces (Artist.cs).
// System.Text.Json serializes record properties as camelCase by default.

export interface ArtistKey {
  artistName: string
}

export interface ArtistMetadata {
  artistKey: ArtistKey
  artistImageUrl: string | null
}

// Mirrors CatalogSyncResult (IArtistCatalogRepo.cs) — returned by POST /catalog/refresh.
export interface CatalogSyncResult {
  upserted: number
  markedAbsent: number
  totalPresent: number
}

// Mirror UnifiedRelatedArtist / UnifiedRelations (RelatedArtist.cs) — returned by GET /related/{artist}.
export interface UnifiedRelatedArtist {
  artistKey: ArtistKey
  imageUrl: string | null
  sources: string[]
}

export interface UnifiedRelations {
  artist: ArtistKey
  related: UnifiedRelatedArtist[]
}

// Mirror DiscoveryCandidate / DiscoveryPage (Discovery.cs) — the per-user swipe queue.
// `sources` is the provenance shown in the UI ("via boygenius, Snail Mail").
export interface DiscoveryCandidate {
  artist: ArtistKey
  imageUrl: string | null
  score: number
  sources: string[]
  depth: number
}

export interface DiscoveryPage {
  items: DiscoveryCandidate[]
  page: number
  pageSize: number
  totalPending: number
}

// The signed-in user, as returned by GET /auth/me (the BFF). Null when not authenticated.
export interface CurrentUser {
  subject: string
  username: string | null
  email: string | null
  displayName: string | null
}
