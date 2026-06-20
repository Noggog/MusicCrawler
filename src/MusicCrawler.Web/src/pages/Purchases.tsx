import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  clearRating,
  downloadPurchase,
  getDownloadStatus,
  getPurchases,
  unsendPurchase,
} from '../api/discovery'
import type { DownloadSnapshot, FeedItem, PurchaseItem } from '../types'
import { useAuth } from '../auth/AuthContext'
import { IconClear } from '../components/icons'

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

function Monitor({ s }: { s: DownloadSnapshot }) {
  const current = s.current[0]
  const activity = current
    ? `⬇ Downloading: ${current.album ?? current.artist.artistName} — ${current.artist.artistName}`
    : s.queued > 0
      ? s.automatic
        ? `Idle — ${s.queued} album${s.queued === 1 ? '' : 's'} queued (auto)`
        : `${s.queued} album${s.queued === 1 ? '' : 's'} queued — use Download now`
      : 'Idle — queue empty'

  return (
    <div className="dl-monitor">
      <div className="dl-monitor-head">
        <span className={s.automatic ? 'dl-badge on' : 'dl-badge off'}>
          {s.automatic ? '● auto' : '○ manual'}
        </span>
        <span className="dl-backend">backend: {s.backend}</span>
      </div>
      <div className={current ? 'dl-activity active' : 'dl-activity'}>{activity}</div>
      <div className="dl-counts">
        <span>Queued <strong>{s.queued}</strong></span>
        <span>Downloading <strong>{s.downloading}</strong></span>
        <span>Ordered <strong>{s.ordered}</strong></span>
        <span>Failed <strong>{s.failed}</strong></span>
      </div>
      <div className="dl-throttle">
        {s.automatic ? 'auto' : 'manual only'} · batch {s.batchSize} · {s.itemDelaySeconds}s between
        items{s.automatic ? ` · every ${s.batchIntervalMinutes}m` : ''}
      </div>
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
    refetchInterval: 5000, // keep the list moving as the drainer works
  })
  const { data: status } = useQuery({
    queryKey: ['download-status'],
    queryFn: getDownloadStatus,
    enabled: !!user,
    refetchInterval: 3000,
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['purchases'] })
    queryClient.invalidateQueries({ queryKey: ['download-status'] })
  }
  const download = useMutation({ mutationFn: (id: string) => downloadPurchase(id), onSuccess: invalidate })
  const unsend = useMutation({ mutationFn: (id: string) => unsendPurchase(id), onSuccess: invalidate })

  // "Nevermind" — clearing the underlying like drops the item from the queue on the next reconcile
  // (the list is derived from liked-but-unowned ratings), so this intercepts an item before download.
  // clearRating only reads artist/album, so a minimal feed item from the row is enough.
  const remove = useMutation({
    mutationFn: (item: PurchaseItem) => {
      const feedItem: FeedItem = {
        kind: item.kind,
        artist: item.artist,
        album: item.album,
        imageUrl: item.imageUrl,
        score: 0,
        sources: [],
        deezerAlbumId: item.deezerAlbumId,
      }
      return clearRating(feedItem)
    },
    onSuccess: () => {
      invalidate()
      queryClient.invalidateQueries({ queryKey: ['ratings'] })
      queryClient.invalidateQueries({ queryKey: ['feed'] })
    },
  })
  const busy = download.isPending || unsend.isPending || remove.isPending

  // The remove (✕) action shared by pending/failed rows — cancels the want before it downloads.
  const removeBtn = (item: PurchaseItem) => (
    <button
      className="disc-btn"
      title="Remove from queue"
      disabled={busy}
      onClick={() => remove.mutate(item)}
    >
      <IconClear />
    </button>
  )

  if (!user) {
    return (
      <section>
        <h1>Download</h1>
        <p><em>Log in to see the albums you've queued to download.</em></p>
      </section>
    )
  }

  const items = data ?? []
  // Only albums are actionable here — they're what the downloader can grab. Liked artists still seed
  // recommendations, but they're managed on the Artists page, not shown as wishlist rows.
  const downloading = items.filter((i) => i.status === 'Downloading' && i.album)
  const pendingAlbums = items.filter((i) => i.status === 'Pending' && i.album)
  const sent = items.filter((i) => i.status === 'Sent' && i.album)
  const failed = items.filter((i) => i.status === 'Failed' && i.album)
  const shownCount = downloading.length + pendingAlbums.length + sent.length + failed.length

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

  return (
    <section>
      <h1>Downloading {shownCount > 0 ? `(${shownCount})` : ''}</h1>

      {status && <Monitor s={status} />}

      {isError && <p className="error">Failed to load wishlist: {(error as Error).message}</p>}
      {isPending && <p><em>Loading…</em></p>}

      {data && shownCount === 0 && (
        <p>
          <em>
            Nothing here yet. Thumbs-up albums on the <Link to="/">Discover</Link> page, or add an
            artist's albums from the <Link to="/artists">Artists</Link> page, to queue them.
          </em>
        </p>
      )}

      {downloading.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Downloading now <span className="feed-count">{downloading.length}</span>
          </h2>
          <div className="disc-list">
            {downloading.map((item) => row(item, <span className="dl-spinner" title="Downloading">⬇</span>))}
          </div>
        </>
      )}

      {failed.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Failed <span className="feed-count">{failed.length}</span>
          </h2>
          <p className="disc-sub"><em>The downloader couldn't grab these — retry.</em></p>
          <div className="disc-list">
            {failed.map((item) =>
              row(
                item,
                <>
                  <button
                    className="disc-btn up"
                    title="Retry download"
                    disabled={busy}
                    onClick={() => download.mutate(item.id)}
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
          <div className="disc-list">
            {pendingAlbums.map((item) =>
              row(
                item,
                <>
                  <button
                    className="disc-btn up"
                    title="Download now"
                    disabled={busy}
                    onClick={() => download.mutate(item.id)}
                  >
                    Download now
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
                <button
                  className="disc-btn"
                  title="Undo — move back to queued"
                  disabled={busy}
                  onClick={() => unsend.mutate(item.id)}
                >
                  Undo
                </button>,
              ),
            )}
          </div>
        </>
      )}
    </section>
  )
}
