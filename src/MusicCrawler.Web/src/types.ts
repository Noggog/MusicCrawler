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

// Mirror FeedKind / DiscoveryStatus / FeedItem / DiscoveryFeedPage / RatedItem (Discovery.cs).
export type FeedKind =
  | 'RecommendedArtist'
  | 'MissingAlbum'
  | 'LibraryArtist'
  | 'RecommendedLibraryArtist'
  | 'SeedLibraryArtist'
export type DiscoveryStatus = 'Pending' | 'Liked' | 'Disliked'

// One thing to react to in the discovery feed. `album` is set only for MissingAlbum items;
// `score`/`sources` rank and explain recommended artists (0/empty otherwise).
export interface FeedItem {
  kind: FeedKind
  artist: ArtistKey
  album: string | null
  imageUrl: string | null
  score: number
  sources: string[]
  // Deezer album id for MissingAlbum items (lets the UI sample/link the album); null otherwise.
  deezerAlbumId: number | null
}

// A paged feed section for a single FeedKind.
export interface DiscoveryFeedPage {
  kind: FeedKind
  items: FeedItem[]
  page: number
  pageSize: number
  total: number
}

// A rating the user has made, for the Ratings review page.
export interface RatedItem {
  kind: FeedKind
  artist: ArtistKey
  album: string | null
  imageUrl: string | null
  verdict: DiscoveryStatus
}

// The signed-in user, as returned by GET /auth/me (the BFF). Null when not authenticated.
export interface CurrentUser {
  subject: string
  username: string | null
  email: string | null
  displayName: string | null
}
