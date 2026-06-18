import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getPurchases, orderPurchase, removePurchase, unsendPurchase } from '../api/discovery'
import type { PurchaseItem } from '../types'
import { useAuth } from '../auth/AuthContext'

function Avatar({ item }: { item: PurchaseItem }) {
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
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const { data, isPending, isError, error } = useQuery({
    queryKey: ['purchases'],
    queryFn: getPurchases,
    enabled: !!user,
  })

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ['purchases'] })
  const order = useMutation({ mutationFn: (id: string) => orderPurchase(id), onSuccess: invalidate })
  const unsend = useMutation({ mutationFn: (id: string) => unsendPurchase(id), onSuccess: invalidate })
  const remove = useMutation({ mutationFn: (id: string) => removePurchase(id), onSuccess: invalidate })
  const busy = order.isPending || unsend.isPending || remove.isPending

  if (!user) {
    return (
      <section>
        <h1>To Buy</h1>
        <p><em>Log in to see the artists and albums you've queued to buy.</em></p>
      </section>
    )
  }

  const items = data ?? []
  const pending = items.filter((i) => i.status === 'Pending')
  const sent = items.filter((i) => i.status === 'Sent')

  const row = (item: PurchaseItem) => (
    <div className="disc-row" key={item.id}>
      <Avatar item={item} />
      <div className="disc-row-main">
        <div className="disc-name">{item.album ?? item.artist.artistName}</div>
        <span className="disc-provenance">
          {item.album
            ? `Album · ${item.artist.artistName}`
            : item.sources.length > 0
              ? `via ${item.sources.slice(0, 3).join(', ')}`
              : 'Artist'}
        </span>
      </div>
      <div className="disc-actions">
        {item.status === 'Pending' ? (
          <button
            className="disc-btn up"
            title="Order — hand to the downloader"
            disabled={busy}
            onClick={() => order.mutate(item.id)}
          >
            Order
          </button>
        ) : (
          <button
            className="disc-btn"
            title="Undo — move back to pending"
            disabled={busy}
            onClick={() => unsend.mutate(item.id)}
          >
            Undo
          </button>
        )}
        <button
          className="disc-btn down"
          title="Remove from the list"
          disabled={busy}
          onClick={() => remove.mutate(item.id)}
        >
          ✕
        </button>
      </div>
    </div>
  )

  return (
    <section>
      <h1>To Buy {items.length > 0 ? `(${items.length})` : ''}</h1>
      <p className="disc-sub">
        <em>
          The shared acquisition queue. Thumbs-up items from <Link to="/">Discover</Link> land here as{' '}
          <strong>pending</strong>; order one to mark it <strong>sent</strong>. Items drop off
          automatically once they appear in the library.
        </em>
      </p>

      {isError && <p className="error">Failed to load wishlist: {(error as Error).message}</p>}
      {isPending && <p><em>Loading…</em></p>}

      {data && items.length === 0 && (
        <p>
          <em>
            Nothing here yet. Thumbs-up artists or albums on the <Link to="/">Discover</Link>{' '}
            page to queue them.
          </em>
        </p>
      )}

      {pending.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Pending <span className="feed-count">{pending.length}</span>
          </h2>
          <div className="disc-list">{pending.map(row)}</div>
        </>
      )}

      {sent.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Ordered <span className="feed-count">{sent.length}</span>
          </h2>
          <p className="disc-sub"><em>Sent to acquire — awaiting arrival in the library.</em></p>
          <div className="disc-list">{sent.map(row)}</div>
        </>
      )}
    </section>
  )
}
