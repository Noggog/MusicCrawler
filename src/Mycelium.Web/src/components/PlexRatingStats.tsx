import { useQuery } from '@tanstack/react-query'
import { getArtistRatings } from '../api/library'

// The user's per-song Plex ratings for an artist — highest / lowest / average across the songs they've
// actually rated — shown to the right of the art in the detail header. Plex only has songs for owned
// artists, so callers gate this to library artists; it also self-hides when the artist is present in
// Plex but nothing's rated, or while the (auth-gated) fetch is still loading/errored. Ratings are on the
// same 0–5 star scale shown in the Plex app.
export function PlexRatingStats({ artist }: { artist: string }) {
  const { data } = useQuery({
    queryKey: ['artist-ratings', artist],
    queryFn: () => getArtistRatings(artist),
    staleTime: 5 * 60 * 1000,
  })

  if (!data || !data.present || data.ratedCount === 0) return null

  const fmt = (n: number) => n.toFixed(1)
  return (
    <div
      className="plex-ratings"
      title={`Your Plex ratings — ${data.ratedCount} of ${data.trackCount} songs rated`}
    >
      <div className="plex-ratings-title">Your Plex ratings</div>
      <div className="plex-ratings-row">
        <div className="plex-rating-stat">
          <span className="plex-rating-val">{fmt(data.average!)}</span>
          <span className="plex-rating-label">avg ★</span>
        </div>
        <div className="plex-rating-stat">
          <span className="plex-rating-val">{fmt(data.highest!)}</span>
          <span className="plex-rating-label">high</span>
        </div>
        <div className="plex-rating-stat">
          <span className="plex-rating-val">{fmt(data.lowest!)}</span>
          <span className="plex-rating-label">low</span>
        </div>
      </div>
      <div className="plex-ratings-count">{data.ratedCount} of {data.trackCount} rated</div>
    </div>
  )
}
