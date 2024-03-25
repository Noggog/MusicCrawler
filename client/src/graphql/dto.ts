interface ArtistKey {
  __typename: string;
  artistName: string;
}

interface Recommendation {
  __typename: string;
  key: ArtistKey;
  sourceArtists: ArtistKey[];
}

export interface RecommendationResponse {
  recommendations: Recommendation[];
}
