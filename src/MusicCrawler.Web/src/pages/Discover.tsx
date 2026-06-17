import { useState } from 'react'
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getMixedFeed, rate, refreshQueue, type Verdict } from '../api/discovery'
import type { FeedItem, FeedKind } from '../types'
import { useAuth } from '../auth/AuthContext'
import { DeezerSample } from '../components/DeezerSample'

const PAGE_SIZE = 20

// The categories that can take part in the mixed feed, with the checkbox label and the badge shown
// on each card so it's obvious what action a card is asking for.
const CATEGORIES: { kind: FeedKind; label: string; badge: string }[] = [
  { kind: 'RecommendedArtist', label: 'New recommended artists', badge: 'Consider new artist' },
  { kind: 'MissingAlbum', label: 'Missing albums', badge: 'Add missing album?' },
  { kind: 'LibraryArtist', label: 'Owned artists to rate', badge: 'Mark existing artist' },
]

const BADGE: Record<FeedKind, string> = Object.fromEntries(
  CATEGORIES.map((c) => [c.kind, c.badge]),
) as Record<FeedKind, string>

const ALL_KINDS = CATEGORIES.map((c) => c.kind)

const newSeed = () => Math.floor(Math.random() * 1_000_000_000)

// The image a source supplied (artist photo or album art), or a coloured initial when missing.
function FeedAvatar({ item, size }: { item: FeedItem; size: number }) {
  const label = item.album ?? item.artist.artistName
  if (item.imageUrl) {
    return <img className="disc-avatar" src={item.imageUrl} alt={label} width={size} height={size} />
  }
  return (
    <div className="disc-avatar disc-avatar-fallback" style={{ width: size, height: size, fontSize: size / 2.5 }}>
      {label.charAt(0).toUpperCase()}
    </div>
  )
}

// "via boygenius, Snail Mail (+2)" — the frontier artists that recommended this candidate.
function Provenance({ sources }: { sources: string[] }) {
  if (sources.length === 0) return null
  const shown = sources.slice(0, 3).join(', ')
  const extra = sources.length > 3 ? ` (+${sources.length - 3})` : ''
  return (
    <span className="disc-provenance">
      via {shown}
      {extra}
    </span>
  )
}

export default function Discover() {
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [shown, setShown] = useState<Set<FeedKind>>(() => new Set<FeedKind>(ALL_KINDS))
  const [page, setPage] = useState(0)
  const [seed, setSeed] = useState(newSeed)
  // Artists whose Deezer player is expanded (kept collapsed by default so we don't mount many iframes).
  const [playing, setPlaying] = useState<Set<string>>(new Set())

  // Keep a stable, sorted kinds list so the query key (and the server's interleave) are deterministic.
  const kinds = ALL_KINDS.filter((k) => shown.has(k))

  const togglePlay = (key: string) =>
    setPlaying((prev) => {
      const next = new Set(prev)
      next.has(key) ? next.delete(key) : next.add(key)
      return next
    })

  const toggleCategory = (kind: FeedKind) => {
    setShown((prev) => {
      const next = new Set(prev)
      next.has(kind) ? next.delete(kind) : next.add(kind)
      return next
    })
    setPage(0)
  }

  const { data, isPending, isError, error, isFetching } = useQuery({
    queryKey: ['feed', 'mixed', kinds.join(','), page, seed],
    queryFn: () => getMixedFeed(kinds, page, PAGE_SIZE, seed),
    enabled: !!user && kinds.length > 0,
    placeholderData: keepPreviousData,
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['feed'] })
    queryClient.invalidateQueries({ queryKey: ['purchases'] })
    queryClient.invalidateQueries({ queryKey: ['ratings'] })
  }

  const rateMutation = useMutation({
    mutationFn: ({ item, verdict }: { item: FeedItem; verdict: Verdict }) => rate(item, verdict),
    onSuccess: invalidate,
  })
  const rebuild = useMutation({
    mutationFn: refreshQueue,
    onSuccess: () => {
      setPage(0)
      invalidate()
    },
  })

  const shuffle = () => {
    setSeed(newSeed())
    setPage(0)
  }

  const busy = rateMutation.isPending || rebuild.isPending
  const items = data?.items ?? []
  const total = data?.total ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  if (!user) {
    return (
      <section>
        <h1>Discover</h1>
        <p><em>Log in to build your personal recommendation feed.</em></p>
      </section>
    )
  }

  return (
    <section>
      <div className="disc-header">
        <h1>Discover</h1>
        <button className="disc-rebuild" onClick={shuffle} disabled={busy || isFetching}>
          ⤮ Shuffle
        </button>
        <button className="disc-rebuild" onClick={() => rebuild.mutate()} disabled={busy}>
          {rebuild.isPending ? 'Rebuilding…' : 'Rebuild recommendations'}
        </button>
      </div>

      <div className="feed-filters">
        {CATEGORIES.map((c) => (
          <label key={c.kind} className="feed-filter">
            <input type="checkbox" checked={shown.has(c.kind)} onChange={() => toggleCategory(c.kind)} />
            {c.label}
          </label>
        ))}
      </div>

      <p className="disc-sub">
        <em>
          A mix of things to react to. 👍 / 👎 each one — adjust anytime on the{' '}
          <Link to="/ratings">Ratings</Link> page. {total} in the feed.
        </em>
      </p>

      {rebuild.isError && <p className="error">Rebuild failed: {(rebuild.error as Error).message}</p>}
      {rateMutation.isError && <p className="error">Rating failed: {(rateMutation.error as Error).message}</p>}
      {isError && <p className="error">Failed to load feed: {(error as Error).message}</p>}

      {kinds.length === 0 && (
        <p><em>Pick at least one category above to see things to react to.</em></p>
      )}

      {kinds.length > 0 && isPending && <p><em>Loading…</em></p>}

      {kinds.length > 0 && !isPending && total === 0 && (
        <p>
          <em>
            Nothing in the feed. Thumb some bands on the <Link to="/artists">Artists</Link> page to
            seed recommendations, or check more categories above.
          </em>
        </p>
      )}

      {items.length > 0 && (
        <div className={isFetching ? 'disc-list fetching' : 'disc-list'}>
          {items.map((item) => {
            const name = item.artist.artistName
            const isAlbum = !!item.album
            const rowKey = isAlbum ? `${name}::${item.album}` : `${item.kind}:${name}`
            const isPlaying = playing.has(rowKey)
            return (
              <div className="disc-row-wrap" key={rowKey}>
                <div className="disc-row">
                  <FeedAvatar item={item} size={56} />
                  <div className="disc-row-main">
                    <span className={`feed-badge feed-badge-${item.kind}`}>{BADGE[item.kind]}</span>
                    <div className="disc-name">{item.album ?? name}</div>
                    {isAlbum ? (
                      <span className="disc-provenance">{name}</span>
                    ) : (
                      <Provenance sources={item.sources} />
                    )}
                  </div>
                  <div className="disc-actions">
                    {!isAlbum && (
                      <button
                        className={isPlaying ? 'disc-btn play active' : 'disc-btn play'}
                        title={isPlaying ? 'Hide Deezer player' : 'Listen on Deezer'}
                        onClick={() => togglePlay(rowKey)}
                      >
                        {isPlaying ? '▾' : '▶'}
                      </button>
                    )}
                    <button
                      className="disc-btn up"
                      title={isAlbum ? 'Queue album to buy' : 'Thumbs up'}
                      disabled={busy}
                      onClick={() => rateMutation.mutate({ item, verdict: 'up' })}
                    >
                      👍
                    </button>
                    <button
                      className="disc-btn down"
                      title="Not interested"
                      disabled={busy}
                      onClick={() => rateMutation.mutate({ item, verdict: 'down' })}
                    >
                      👎
                    </button>
                  </div>
                </div>
                {isPlaying && !isAlbum && <DeezerSample artist={name} />}
              </div>
            )
          })}
        </div>
      )}

      {pageCount > 1 && (
        <div className="disc-pager">
          <button disabled={page === 0 || isFetching} onClick={() => setPage((p) => p - 1)}>
            ‹ prev
          </button>
          <span>
            page {page + 1} / {pageCount}
          </span>
          <button disabled={page >= pageCount - 1 || isFetching} onClick={() => setPage((p) => p + 1)}>
            next ›
          </button>
        </div>
      )}
    </section>
  )
}
