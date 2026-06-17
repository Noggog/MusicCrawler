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
