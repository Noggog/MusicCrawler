import { useEffect, useState } from 'react'
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { getMixedFeed, rate, refreshQueue, type Verdict } from '../api/discovery'
import { getDeezerPlayInfo } from '../api/deezer'
import type { FeedItem, FeedKind } from '../types'
import { useAuth } from '../auth/AuthContext'
import { DeezerSample } from '../components/DeezerSample'

const PAGE_SIZE = 20

// The badge shown on each card so it's obvious what action a card is asking for, keyed by kind.
const BADGE: Record<FeedKind, string> = {
  RecommendedArtist: 'Consider new artist',
  MissingAlbum: 'Add missing album',
  RecommendedLibraryArtist: 'Rate existing artist',
  SeedLibraryArtist: 'Seed: rate existing artist',
  LibraryArtist: 'Mark existing artist',
}

// The filter tree. "Recommended Artists" groups the two recommended sections (existing owned vs.
// brand-new), each individually toggleable; the standalone rows sit beside it.
const RECOMMENDED_KINDS: FeedKind[] = ['RecommendedLibraryArtist', 'RecommendedArtist']
const FILTER_GROUP: { label: string; kind: FeedKind }[] = [
  { label: 'Existing', kind: 'RecommendedLibraryArtist' },
  { label: 'New', kind: 'RecommendedArtist' },
]
const STANDALONE_FILTERS: { label: string; kind: FeedKind }[] = [
  { label: 'Missing albums', kind: 'MissingAlbum' },
  { label: 'Unknown existing artists', kind: 'SeedLibraryArtist' },
]

const ALL_KINDS: FeedKind[] = ['RecommendedArtist', 'MissingAlbum', 'RecommendedLibraryArtist', 'SeedLibraryArtist']
// Default to the recommended sections (new + existing owned) plus missing albums. The seed section
// (owned artists nothing recommends yet) stays off until opted in — it's noisier and less targeted.
const DEFAULT_KINDS: FeedKind[] = ['RecommendedArtist', 'MissingAlbum', 'RecommendedLibraryArtist']

const newSeed = () => Math.floor(Math.random() * 1_000_000_000)

// Stable identity for a feed row, shared by render and the rate mutation so a rated row can be
// marked in place. Albums key on artist+album; artists on kind+name.
const rowKeyFor = (item: FeedItem) =>
  item.album ? `${item.artist.artistName}::${item.album}` : `${item.kind}:${item.artist.artistName}`

// The image a source supplied (artist photo or album art), or a coloured initial when missing.
function FeedAvatar({ item, size }: { item: FeedItem; size: number }) {
  const label = item.album ?? item.artist.artistName
  const isArtist = !item.album
  // Existing/library artists usually carry no photo of their own — attempt a Deezer lookup for one.
  // Cached and shared with the sample player; falls back to the coloured initial on a Deezer miss.
  const { data: deezer } = useQuery({
    queryKey: ['deezer-play', item.artist.artistName],
    queryFn: () => getDeezerPlayInfo(item.artist.artistName),
    enabled: isArtist && !item.imageUrl,
    staleTime: 60 * 60 * 1000,
  })
  const src = item.imageUrl ?? (isArtist ? deezer?.imageUrl ?? null : null)
  if (src) {
    return <img className="disc-avatar" src={src} alt={label} width={size} height={size} />
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
  const [shown, setShown] = useState<Set<FeedKind>>(() => new Set<FeedKind>(DEFAULT_KINDS))
  const [page, setPage] = useState(0)
  const [seed, setSeed] = useState(newSeed)
  // Artists whose Deezer player is expanded (kept collapsed by default so we don't mount many iframes).
  const [playing, setPlaying] = useState<Set<string>>(new Set())
  // Rows rated this view, by row key -> verdict. They stay in place (marked, not removed) until the
  // next natural refresh, so a 👍/👎 doesn't reflow the whole list out from under you.
  const [rated, setRated] = useState<Map<string, Verdict>>(new Map())

  // Keep a stable, sorted kinds list so the query key (and the server's interleave) are deterministic.
  const kinds = ALL_KINDS.filter((k) => shown.has(k))

  // A natural refresh (page, shuffle, or category change refetches the feed) clears the in-place
  // marks so the freshly-fetched list — which already excludes the rated items — starts clean.
  const kindsKey = kinds.join(',')
  useEffect(() => {
    setRated(new Map())
  }, [page, seed, kindsKey])

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

  // The parent "Recommended Artists" checkbox: clear both children if all are on, else turn both on.
  const recommendedAllOn = RECOMMENDED_KINDS.every((k) => shown.has(k))
  const recommendedSomeOn = RECOMMENDED_KINDS.some((k) => shown.has(k))
  const toggleRecommendedGroup = () => {
    setShown((prev) => {
      const next = new Set(prev)
      const turnOff = RECOMMENDED_KINDS.every((k) => next.has(k))
      RECOMMENDED_KINDS.forEach((k) => (turnOff ? next.delete(k) : next.add(k)))
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
    // Mark the row in place immediately and leave the feed query alone — invalidating it here is
    // what made the list re-interleave and jump. The mark drops away on the next natural refresh.
    onMutate: ({ item, verdict }) => {
      setRated((prev) => new Map(prev).set(rowKeyFor(item), verdict))
    },
    onError: (_err, { item }) => {
      setRated((prev) => {
        const next = new Map(prev)
        next.delete(rowKeyFor(item))
        return next
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['purchases'] })
      queryClient.invalidateQueries({ queryKey: ['ratings'] })
    },
  })
  const rebuild = useMutation({
    mutationFn: refreshQueue,
    onSuccess: () => {
      setRated(new Map())
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
        <div className="feed-filter-group">
          <label className="feed-filter">
            <input
              type="checkbox"
              checked={recommendedAllOn}
              ref={(el) => {
                if (el) el.indeterminate = recommendedSomeOn && !recommendedAllOn
              }}
              onChange={toggleRecommendedGroup}
            />
            Recommended Artists
          </label>
          <div className="feed-subfilters">
            {FILTER_GROUP.map((c) => (
              <label key={c.kind} className="feed-filter">
                <input type="checkbox" checked={shown.has(c.kind)} onChange={() => toggleCategory(c.kind)} />
                {c.label}
              </label>
            ))}
          </div>
        </div>
        {STANDALONE_FILTERS.map((c) => (
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
            // Artists sample by name; albums sample by their Deezer id (when we have one).
            const canPlay = isAlbum ? !!item.deezerAlbumId : true
            const verdict = rated.get(rowKey)
            return (
              <div className={verdict ? 'disc-row-wrap rated' : 'disc-row-wrap'} key={rowKey}>
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
                    {canPlay && (
                      <button
                        className={isPlaying ? 'disc-btn play active' : 'disc-btn play'}
                        title={isPlaying ? 'Hide Deezer player' : 'Listen on Deezer'}
                        onClick={() => togglePlay(rowKey)}
                      >
                        {isPlaying ? '▾' : '▶'}
                      </button>
                    )}
                    {verdict ? (
                      <span className="disc-rated" title="Rated — refreshes on shuffle or page change">
                        {verdict === 'up' ? '👍 Added' : '👎 Dismissed'}
                      </span>
                    ) : (
                      <>
                        <button
                          className="disc-btn up"
                          title={isAlbum ? 'Queue album to buy' : 'Thumbs up'}
                          disabled={rebuild.isPending}
                          onClick={() => rateMutation.mutate({ item, verdict: 'up' })}
                        >
                          👍
                        </button>
                        <button
                          className="disc-btn down"
                          title="Not interested"
                          disabled={rebuild.isPending}
                          onClick={() => rateMutation.mutate({ item, verdict: 'down' })}
                        >
                          👎
                        </button>
                      </>
                    )}
                  </div>
                </div>
                {isPlaying && (isAlbum
                  ? <DeezerSample albumId={item.deezerAlbumId!} />
                  : <DeezerSample artist={name} />)}
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
