import { useEffect, useState } from 'react'
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  dislikeCandidate,
  getQueue,
  likeCandidate,
  refreshQueue,
} from '../api/discovery'
import type { DiscoveryCandidate } from '../types'
import { useAuth } from '../auth/AuthContext'
import { DeezerSample } from '../components/DeezerSample'

const PAGE_SIZE = 20

type ViewMode = 'list' | 'swipe'

// The image a source supplied, or a coloured initial when there isn't one.
function Avatar({ candidate, size }: { candidate: DiscoveryCandidate; size: number }) {
  const name = candidate.artist.artistName
  if (candidate.imageUrl) {
    return <img className="disc-avatar" src={candidate.imageUrl} alt={name} width={size} height={size} />
  }
  return (
    <div className="disc-avatar disc-avatar-fallback" style={{ width: size, height: size, fontSize: size / 2.5 }}>
      {name.charAt(0).toUpperCase()}
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
  const [mode, setMode] = useState<ViewMode>('list')
  const [page, setPage] = useState(0)
  // Artists whose Deezer player is expanded in the list view (kept collapsed by default so we
  // don't mount 20 iframes at once).
  const [playing, setPlaying] = useState<Set<string>>(new Set())

  const togglePlay = (artist: string) =>
    setPlaying((prev) => {
      const next = new Set(prev)
      next.has(artist) ? next.delete(artist) : next.add(artist)
      return next
    })

  // Swipe mode always works the top of the queue, so it pins to page 0.
  const activePage = mode === 'swipe' ? 0 : page

  const { data, isPending, isError, error, isFetching } = useQuery({
    queryKey: ['discovery', activePage],
    queryFn: () => getQueue(activePage, PAGE_SIZE),
    enabled: !!user,
    placeholderData: keepPreviousData,
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['discovery'] })
    queryClient.invalidateQueries({ queryKey: ['purchases'] })
  }

  const like = useMutation({ mutationFn: likeCandidate, onSuccess: invalidate })
  const dislike = useMutation({ mutationFn: dislikeCandidate, onSuccess: invalidate })
  const rebuild = useMutation({
    mutationFn: () => refreshQueue(0, PAGE_SIZE),
    onSuccess: () => {
      setPage(0)
      invalidate()
    },
  })

  const busy = like.isPending || dislike.isPending || rebuild.isPending
  const items = data?.items ?? []
  const total = data?.totalPending ?? 0
  const pageCount = Math.max(1, Math.ceil(total / PAGE_SIZE))

  // In swipe mode the top candidate is always the one on screen; ←/→ decide it.
  const current = items[0]
  useEffect(() => {
    if (mode !== 'swipe' || !current || busy) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowRight') like.mutate(current.artist.artistName)
      else if (e.key === 'ArrowLeft') dislike.mutate(current.artist.artistName)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [mode, current, busy, like, dislike])

  if (!user) {
    return (
      <section>
        <h1>Discover</h1>
        <p><em>Log in to build your personal recommendation queue.</em></p>
      </section>
    )
  }

  return (
    <section>
      <div className="disc-header">
        <h1>Discover</h1>
        <div className="disc-toggle" role="tablist" aria-label="View mode">
          <button
            className={mode === 'list' ? 'active' : ''}
            onClick={() => setMode('list')}
            role="tab"
            aria-selected={mode === 'list'}
          >
            List
          </button>
          <button
            className={mode === 'swipe' ? 'active' : ''}
            onClick={() => setMode('swipe')}
            role="tab"
            aria-selected={mode === 'swipe'}
          >
            Swipe
          </button>
        </div>
        <button className="disc-rebuild" onClick={() => rebuild.mutate()} disabled={busy}>
          {rebuild.isPending ? 'Rebuilding…' : 'Rebuild from seeds'}
        </button>
      </div>

      <p className="disc-sub">
        <em>
          Grown from your seeds along the similarity graph. 👍 queues an artist to buy and pulls in
          who <em>they</em> sound like; 👎 prunes them. {total} waiting.
        </em>
      </p>

      {isError && <p className="error">Failed to load queue: {(error as Error).message}</p>}
      {rebuild.isError && <p className="error">Rebuild failed: {(rebuild.error as Error).message}</p>}

      {isPending && <p><em>Loading…</em></p>}

      {!isPending && total === 0 && (
        <p>
          <em>
            Nothing queued yet. Mark some artists as seeds on the{' '}
            <Link to="/artists">Artists</Link> page, then hit “Rebuild from seeds”.
          </em>
        </p>
      )}

      {mode === 'list' && total > 0 && (
        <>
          <div className={isFetching ? 'disc-list fetching' : 'disc-list'}>
            {items.map((c) => {
              const name = c.artist.artistName
              const isPlaying = playing.has(name)
              return (
                <div className="disc-row-wrap" key={name}>
                  <div className="disc-row">
                    <Avatar candidate={c} size={56} />
                    <div className="disc-row-main">
                      <div className="disc-name">{name}</div>
                      <Provenance sources={c.sources} />
                    </div>
                    <div className="disc-actions">
                      <button
                        className={isPlaying ? 'disc-btn play active' : 'disc-btn play'}
                        title={isPlaying ? 'Hide Deezer player' : 'Listen on Deezer'}
                        onClick={() => togglePlay(name)}
                      >
                        {isPlaying ? '▾' : '▶'}
                      </button>
                      <button
                        className="disc-btn up"
                        title="Queue to buy & find more like this"
                        disabled={busy}
                        onClick={() => like.mutate(name)}
                      >
                        👍
                      </button>
                      <button
                        className="disc-btn down"
                        title="Not interested"
                        disabled={busy}
                        onClick={() => dislike.mutate(name)}
                      >
                        👎
                      </button>
                    </div>
                  </div>
                  {isPlaying && <DeezerSample artist={name} />}
                </div>
              )
            })}
          </div>

          {pageCount > 1 && (
            <div className="disc-pager">
              <button disabled={activePage === 0 || isFetching} onClick={() => setPage((p) => p - 1)}>
                ‹ prev
              </button>
              <span>
                page {activePage + 1} / {pageCount}
              </span>
              <button
                disabled={activePage >= pageCount - 1 || isFetching}
                onClick={() => setPage((p) => p + 1)}
              >
                next ›
              </button>
            </div>
          )}
        </>
      )}

      {mode === 'swipe' && current && (
        <div className="disc-swipe">
          <div className="disc-card">
            <Avatar candidate={current} size={220} />
            <div className="disc-card-name">{current.artist.artistName}</div>
            <Provenance sources={current.sources} />
            <DeezerSample artist={current.artist.artistName} />
          </div>
          <div className="disc-swipe-actions">
            <button className="disc-btn down big" disabled={busy} onClick={() => dislike.mutate(current.artist.artistName)}>
              👎
            </button>
            <button className="disc-btn up big" disabled={busy} onClick={() => like.mutate(current.artist.artistName)}>
              👍
            </button>
          </div>
          <p className="disc-hint"><em>← skip · → buy &amp; expand · {total} left</em></p>
        </div>
      )}
    </section>
  )
}
