import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getPurchases } from '../api/discovery'
import type { FeedItem } from '../types'
import { useAuth } from '../auth/AuthContext'

function Avatar({ item }: { item: FeedItem }) {
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

export default function Purchases() {
  const { user } = useAuth()
  const { data, isPending, isError, error } = useQuery({
    queryKey: ['purchases'],
    queryFn: getPurchases,
    enabled: !!user,
  })

  if (!user) {
    return (
      <section>
        <h1>To Buy</h1>
        <p><em>Log in to see the artists and albums you've queued to buy.</em></p>
      </section>
    )
  }

  const items = data ?? []
  const artists = items.filter((i) => !i.album)
  const albums = items.filter((i) => i.album)

  return (
    <section>
      <h1>To Buy {items.length > 0 ? `(${items.length})` : ''}</h1>

      {isError && <p className="error">Failed to load wishlist: {(error as Error).message}</p>}
      {isPending && <p><em>Loading…</em></p>}

      {data && items.length === 0 && (
        <p>
          <em>
            Nothing here yet. Thumbs-up artists or albums on the <Link to="/discover">Discover</Link>{' '}
            page to queue them.
          </em>
        </p>
      )}

      {artists.length > 0 && (
        <>
          <h2 className="feed-section-title">Artists <span className="feed-count">{artists.length}</span></h2>
          <div className="disc-list">
            {artists.map((item) => (
              <div className="disc-row" key={item.artist.artistName}>
                <Avatar item={item} />
                <div className="disc-row-main">
                  <div className="disc-name">{item.artist.artistName}</div>
                  {item.sources.length > 0 && (
                    <span className="disc-provenance">via {item.sources.slice(0, 3).join(', ')}</span>
                  )}
                </div>
              </div>
            ))}
          </div>
        </>
      )}

      {albums.length > 0 && (
        <>
          <h2 className="feed-section-title">Albums <span className="feed-count">{albums.length}</span></h2>
          <div className="disc-list">
            {albums.map((item) => (
              <div className="disc-row" key={`${item.artist.artistName}::${item.album}`}>
                <Avatar item={item} />
                <div className="disc-row-main">
                  <div className="disc-name">{item.album}</div>
                  <span className="disc-provenance">{item.artist.artistName}</span>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </section>
  )
}
