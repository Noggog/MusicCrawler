import { useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  clearRating,
  downloadPurchase,
  getDownloadStatus,
  getLibraryAlbums,
  getPurchases,
  mergePurchase,
  unsendPurchase,
} from '../api/discovery'
import { useArtAccent } from '../art/artColors'
import type { DownloadSnapshot, FeedItem, PurchaseItem } from '../types'
import { useAuth } from '../auth/AuthContext'
import { IconCheck, IconClear, IconDownload, IconUndo, IconWrench, IconX } from '../components/icons'

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

// A queue row, themed from its album/artist art (same `--art-accent` plumbing as the Discover feed):
// the shared `.disc-row` styling turns that into the tinted background + border + glow automatically.
function PurchaseRow({ item, actions }: { item: PurchaseItem; actions: ReactNode }) {
  const accent = useArtAccent(item.imageUrl)
  const accentStyle = accent ? ({ '--art-accent': accent } as CSSProperties) : undefined
  return (
    <div className="disc-row" style={accentStyle}>
      <Avatar item={item} />
      {/* The name/provenance block deep-links into Browse, opened + filtered to this artist
          (the same /browse?artist= mechanism the Discover "Go to artist" link uses) so you can
          jump from a queued album to the artist's full readout. */}
      <Link
        className="disc-row-main disc-row-link"
        to={`/browse?artist=${encodeURIComponent(item.artist.artistName)}`}
        title={`Go to ${item.artist.artistName} in Browse`}
      >
        <div className="disc-name">{item.album ?? item.artist.artistName}</div>
        <span className="disc-provenance">
          {item.album
            ? `Album · ${item.artist.artistName}`
            : item.sources.length > 0
              ? `Artist · via ${item.sources.slice(0, 3).join(', ')}`
              : 'Artist'}
        </span>
      </Link>
      <div className="disc-actions">{actions}</div>
    </div>
  )
}

// "Already in library?" — resolve a near-miss title mismatch by hand. Lists the albums the library
// already owns under this act (Deezer's "DOOM (Original Game Soundtrack)" won't auto-match Plex's
// "Doom: Original Game Soundtrack"); picking one records a durable merge so the row leaves the queue
// and never resurfaces as missing. Uses the app's shared picker-modal chrome.
function MergePane({
  item,
  onClose,
  onMerged,
}: {
  item: PurchaseItem
  onClose: () => void
  onMerged: () => void
}) {
  const albums = useQuery({
    queryKey: ['library-albums', item.id],
    queryFn: () => getLibraryAlbums(item.id),
  })
  const merge = useMutation({
    mutationFn: (libraryAlbum: string) => mergePurchase(item.id, libraryAlbum),
    onSuccess: onMerged,
  })

  return (
    <div className="picker-backdrop" onClick={onClose}>
      <div className="picker-panel" onClick={(e) => e.stopPropagation()}>
        <div className="picker-head">
          <h2>Match Existing Album</h2>
          <button className="disc-btn" title="Close" onClick={onClose}>
            <IconX />
          </button>
        </div>
        <p className="picker-pinned">Merge “{item.album}” into an album already in your library.</p>

        {albums.isPending && <p><em>Loading library…</em></p>}
        {albums.isError && <p className="error">Failed to load library albums.</p>}
        {albums.data && albums.data.length === 0 && (
          <p><em>No albums are in the library under {item.artist.artistName}.</em></p>
        )}

        <ul className="picker-results">
          {(albums.data ?? []).map((title) => (
            <li key={title} className="picker-result">
              <div className="picker-meta">
                <span className="picker-name">{title}</span>
              </div>
              <button
                className="disc-btn up"
                title="Merge into this album"
                disabled={merge.isPending}
                onClick={() => merge.mutate(title)}
              >
                <IconCheck />
              </button>
            </li>
          ))}
        </ul>

        {merge.isError && <p className="error">Merge failed — try again.</p>}
      </div>
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
  const [mergingId, setMergingId] = useState<string | null>(null)

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

  // "Already in library?" — opens the merge pane to reconcile a near-miss title against an album
  // Plex already has (which is why it's stuck in the queue rather than flipping to in-library).
  const mergeBtn = (item: PurchaseItem) => (
    <button
      className="disc-btn"
      title="Match an album already in the library"
      disabled={busy}
      onClick={() => setMergingId(item.id)}
    >
      <IconWrench />
    </button>
  )
  const mergingItem = mergingId ? (data ?? []).find((i) => i.id === mergingId) : undefined

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
    <PurchaseRow key={item.id} item={item} actions={actions} />
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
            artist's albums from the <Link to="/browse">Browse</Link> page, to queue them.
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
                    <IconDownload />
                  </button>
                  {mergeBtn(item)}
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
                  {mergeBtn(item)}
                  <button
                    className="disc-btn"
                    title="Undo — move back to queued"
                    disabled={busy}
                    onClick={() => unsend.mutate(item.id)}
                  >
                    <IconUndo />
                  </button>
                </>,
              ),
            )}
          </div>
        </>
      )}

      {mergingItem && (
        <MergePane
          item={mergingItem}
          onClose={() => setMergingId(null)}
          onMerged={() => {
            setMergingId(null)
            invalidate()
            queryClient.invalidateQueries({ queryKey: ['ratings'] })
            queryClient.invalidateQueries({ queryKey: ['feed'] })
          }}
        />
      )}
    </section>
  )
}
