import type { ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  downloadPurchase,
  getDownloadStatus,
  getPurchases,
  unsendPurchase,
} from '../api/discovery'
import type { DownloadSnapshot, PurchaseItem } from '../types'
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
  const busy = download.isPending || unsend.isPending

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
  const downloading = items.filter((i) => i.status === 'Downloading')
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

  return (
    <section>
      <h1>To Buy {items.length > 0 ? `(${items.length})` : ''}</h1>

      {status && <Monitor s={status} />}

      <p className="disc-sub">
        <em>
          The shared acquisition queue. Thumbs-up items from <Link to="/">Discover</Link> land here.
          Hit <strong>Download now</strong> on an album to grab it (or turn on automatic downloads to
          drain the queue in the background); items drop off once they appear in the library. Artists
          are wishlist reminders — only albums download.
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
                <button
                  className="disc-btn up"
                  title="Retry download"
                  disabled={busy}
                  onClick={() => download.mutate(item.id)}
                >
                  Retry
                </button>,
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
                <button
                  className="disc-btn up"
                  title="Download now"
                  disabled={busy}
                  onClick={() => download.mutate(item.id)}
                >
                  Download now
                </button>,
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

      {pendingArtists.length > 0 && (
        <>
          <h2 className="feed-section-title">
            Artists — wishlist <span className="feed-count">{pendingArtists.length}</span>
          </h2>
          <p className="disc-sub"><em>Not auto-downloaded. Queue specific albums to actually grab them.</em></p>
          <div className="disc-list">{pendingArtists.map((item) => row(item, null))}</div>
        </>
      )}
    </section>
  )
}
