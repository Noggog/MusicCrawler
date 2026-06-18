import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  getPurchases,
  orderPurchase,
  removePurchase,
  retryPurchase,
  unsendPurchase,
} from '../api/discovery'
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
  const retry = useMutation({ mutationFn: (id: string) => retryPurchase(id), onSuccess: invalidate })
  const remove = useMutation({ mutationFn: (id: string) => removePurchase(id), onSuccess: invalidate })
  const busy = order.isPending || unsend.isPending || retry.isPending || remove.isPending

  if (!user) {
    return (
      <section>
        <h1>To Buy</h1>
        <p><em>Log in to see the artists and albums you've queued to buy.</em></p>
      </section>
    )
  }

  const items = data ?? []
  // Albums are what the downloader can actually grab; artists are wishlist reminders only.
  const pendingAlbums = items.filter((i) => i.status === 'Pending' && i.album)
  const pendingArtists = items.filter((i) => i.status === 'Pending' && !i.album)
  const sent = items.filter((i) => i.status === 'Sent')
  const failed = items.filter((i) => i.status === 'Failed')

  const row = (item: PurchaseItem, actions: ReactNode) => (
    <div className="disc-row" key={item.id}>
      <Avatar item={item} />
      <div className="disc-row-main">
        <div className="disc-name">{item.album ?? item.artist.artistName}</div>
        <span className="disc-provenance">
          {item.album
            ? `Album · ${item.artist.artistName}`
            : item.sources.length > 0
              ? `Artist · via ${item.sources.slice(0, 3).join(', ')}`
              : 'Artist'}
        </span>
      </div>
      <div className="disc-actions">{actions}</div>
    </div>
  )

  const removeBtn = (item: PurchaseItem) => (
    <button
      className="disc-btn down"
      title="Remove from the list"
      disabled={busy}
      onClick={() => remove.mutate(item.id)}
    >
      ✕
    </button>
  )

  return (
    <section>
      <h1>To Buy {items.length > 0 ? `(${items.length})` : ''}</h1>
      <p className="disc-sub">
        <em>
          The shared acquisition queue. Thumbs-up items from <Link to="/">Discover</Link> land here.
          Albums are downloaded automatically by the slow background drainer (or hit{' '}
          <strong>Order</strong> to prioritize one); they drop off once they appear in the library.
          Artists are wishlist reminders — they aren't auto-downloaded.
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

      {failed.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Failed <span className="feed-count">{failed.length}</span>
          </h2>
          <p className="disc-sub"><em>The downloader couldn't grab these — retry or remove.</em></p>
          <div className="disc-list">
            {failed.map((item) =>
              row(
                item,
                <>
                  <button
                    className="disc-btn up"
                    title="Retry download"
                    disabled={busy}
                    onClick={() => retry.mutate(item.id)}
                  >
                    Retry
                  </button>
                  {removeBtn(item)}
                </>,
              ),
            )}
          </div>
        </>
      )}

      {pendingAlbums.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Albums — queued <span className="feed-count">{pendingAlbums.length}</span>
          </h2>
          <div className="disc-list">
            {pendingAlbums.map((item) =>
              row(
                item,
                <>
                  <button
                    className="disc-btn up"
                    title="Download now (prioritize)"
                    disabled={busy}
                    onClick={() => order.mutate(item.id)}
                  >
                    Order
                  </button>
                  {removeBtn(item)}
                </>,
              ),
            )}
          </div>
        </>
      )}

      {sent.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Ordered <span className="feed-count">{sent.length}</span>
          </h2>
          <p className="disc-sub"><em>Downloaded — awaiting arrival in the library.</em></p>
          <div className="disc-list">
            {sent.map((item) =>
              row(
                item,
                <>
                  <button
                    className="disc-btn"
                    title="Undo — move back to queued"
                    disabled={busy}
                    onClick={() => unsend.mutate(item.id)}
                  >
                    Undo
                  </button>
                  {removeBtn(item)}
                </>,
              ),
            )}
          </div>
        </>
      )}

      {pendingArtists.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Artists — wishlist <span className="feed-count">{pendingArtists.length}</span>
          </h2>
          <p className="disc-sub"><em>Not auto-downloaded. Queue specific albums to actually grab them.</em></p>
          <div className="disc-list">{pendingArtists.map((item) => row(item, removeBtn(item)))}</div>
        </>
      )}
    </section>
  )
}
