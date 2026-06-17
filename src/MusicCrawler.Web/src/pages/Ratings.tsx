import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { clearRating, getRatings, rate, type Verdict } from '../api/discovery'
import type { RatedItem } from '../types'
import { useAuth } from '../auth/AuthContext'

function Avatar({ item }: { item: RatedItem }) {
  const label = item.album ?? item.artist.artistName
  if (item.imageUrl) {
    return <img className="disc-avatar" src={item.imageUrl} alt={label} width={48} height={48} />
  }
  return (
    <div className="disc-avatar disc-avatar-fallback" style={{ width: 48, height: 48, fontSize: 20 }}>
      {label.charAt(0).toUpperCase()}
    </div>
  )
}

// Ratings only ever come back as RecommendedArtist / LibraryArtist / MissingAlbum (the engine
// collapses owned rated artists to LibraryArtist), but the union covers the discover-only sections
// too, so map them to the same "Owned artist" label to satisfy the exhaustive record.
const KIND_LABEL: Record<RatedItem['kind'], string> = {
  RecommendedArtist: 'Artist',
  LibraryArtist: 'Owned artist',
  RecommendedLibraryArtist: 'Owned artist',
  SeedLibraryArtist: 'Owned artist',
  MissingAlbum: 'Album',
}

export default function Ratings() {
  const queryClient = useQueryClient()
  const { user } = useAuth()

  const { data, isPending, isError, error } = useQuery({
    queryKey: ['ratings'],
    queryFn: getRatings,
    enabled: !!user,
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['ratings'] })
    queryClient.invalidateQueries({ queryKey: ['feed'] })
    queryClient.invalidateQueries({ queryKey: ['purchases'] })
  }

  // Clicking the verdict that's already set clears it back to neutral; otherwise it flips.
  const reRate = useMutation({
    mutationFn: ({ item, verdict }: { item: RatedItem; verdict: Verdict }) => {
      const target = verdict === 'up' ? 'Liked' : 'Disliked'
      return item.verdict === target ? clearRating(item) : rate(item, verdict)
    },
    onSuccess: invalidate,
  })
  const clear = useMutation({ mutationFn: (item: RatedItem) => clearRating(item), onSuccess: invalidate })

  const busy = reRate.isPending || clear.isPending

  if (!user) {
    return (
      <section>
        <h1>Ratings</h1>
        <p><em>Log in to review the artists and albums you’ve rated.</em></p>
      </section>
    )
  }

  const items = data ?? []

  return (
    <section>
      <h1>Ratings {items.length > 0 ? `(${items.length})` : ''}</h1>
      <p className="disc-sub">
        <em>
          Everything you’ve thumbed. Flip a verdict or clear it to send it back to{' '}
          <Link to="/">Discover</Link>. Albums you’ve since acquired drop off automatically.
        </em>
      </p>

      {isError && <p className="error">Failed to load ratings: {(error as Error).message}</p>}
      {isPending && <p><em>Loading…</em></p>}

      {data && items.length === 0 && (
        <p>
          <em>
            No ratings yet. Thumb some artists or albums on the <Link to="/">Discover</Link>{' '}
            page (or rate bands on the <Link to="/artists">Artists</Link> page).
          </em>
        </p>
      )}

      {items.length > 0 && (
        <div className="disc-list">
          {items.map((item) => {
            const name = item.artist.artistName
            const rowKey = item.album ? `${name}::${item.album}` : name
            return (
              <div className="disc-row" key={rowKey}>
                <Avatar item={item} />
                <div className="disc-row-main">
                  <div className="disc-name">{item.album ?? name}</div>
                  <span className="disc-provenance">
                    {KIND_LABEL[item.kind]}
                    {item.album ? ` · ${name}` : ''}
                  </span>
                </div>
                <div className="disc-actions">
                  <button
                    className={item.verdict === 'Liked' ? 'disc-btn up active' : 'disc-btn up'}
                    title={item.verdict === 'Liked' ? 'Clear rating' : 'Thumbs up'}
                    disabled={busy}
                    onClick={() => reRate.mutate({ item, verdict: 'up' })}
                  >
                    👍
                  </button>
                  <button
                    className={item.verdict === 'Disliked' ? 'disc-btn down active' : 'disc-btn down'}
                    title={item.verdict === 'Disliked' ? 'Clear rating' : 'Thumbs down'}
                    disabled={busy}
                    onClick={() => reRate.mutate({ item, verdict: 'down' })}
                  >
                    👎
                  </button>
                  <button
                    className="disc-btn"
                    title="Clear rating"
                    disabled={busy}
                    onClick={() => clear.mutate(item)}
                  >
                    ✕
                  </button>
                </div>
              </div>
            )
          })}
        </div>
      )}
    </section>
  )
}
