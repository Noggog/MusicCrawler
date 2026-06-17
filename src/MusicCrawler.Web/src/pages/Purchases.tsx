import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getPurchases } from '../api/discovery'
import type { DiscoveryCandidate } from '../types'
import { useAuth } from '../auth/AuthContext'

function Avatar({ candidate }: { candidate: DiscoveryCandidate }) {
  const name = candidate.artist.artistName
  if (candidate.imageUrl) {
    return <img className="disc-avatar" src={candidate.imageUrl} alt={name} width={48} height={48} />
  }
  return (
    <div className="disc-avatar disc-avatar-fallback" style={{ width: 48, height: 48, fontSize: 20 }}>
      {name.charAt(0).toUpperCase()}
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
        <p><em>Log in to see the artists you've queued to buy.</em></p>
      </section>
    )
  }

  return (
    <section>
      <h1>To Buy {data && data.length > 0 ? `(${data.length})` : ''}</h1>

      {isError && <p className="error">Failed to load wishlist: {(error as Error).message}</p>}
      {isPending && <p><em>Loading…</em></p>}

      {data && data.length === 0 && (
        <p>
          <em>
            Nothing here yet. Thumbs-up artists on the <Link to="/discover">Discover</Link> page to
            queue them.
          </em>
        </p>
      )}

      {data && data.length > 0 && (
        <div className="disc-list">
          {data.map((c) => (
            <div className="disc-row" key={c.artist.artistName}>
              <Avatar candidate={c} />
              <div className="disc-row-main">
                <div className="disc-name">{c.artist.artistName}</div>
                {c.sources.length > 0 && (
                  <span className="disc-provenance">via {c.sources.slice(0, 3).join(', ')}</span>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}
