import { useEffect, useRef, useState } from 'react'
import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import {
  clearRating,
  getArtistAlbums,
  getMixedFeed,
  rate,
  refreshQueue,
  snooze,
  type SnoozeDuration,
  type Verdict,
} from '../api/discovery'
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
const FILTER_GROUP: { label: string; kind: FeedKind; tip: string }[] = [
  { label: 'Existing', kind: 'RecommendedLibraryArtist', tip: "Recommends artists already in the library that you haven't rated" },
  { label: 'New', kind: 'RecommendedArtist', tip: 'Recommends artists not yet in the library' },
]
const STANDALONE_FILTERS: { label: string; kind: FeedKind; tip: string }[] = [
  { label: 'Missing albums', kind: 'MissingAlbum', tip: 'Recommends missing albums for artists you have rated up' },
  { label: 'Seed Existing Artists', kind: 'SeedLibraryArtist', tip: "Asks you to rate existing artists that aren't yet recommended by anything you like." },
]

const ALL_KINDS: FeedKind[] = ['RecommendedArtist', 'MissingAlbum', 'RecommendedLibraryArtist', 'SeedLibraryArtist']
// Default to everything on: the recommended sections (new + existing owned), missing albums, and the
// seed section (owned artists nothing recommends yet) — rating those grows the frontier.
const DEFAULT_KINDS: FeedKind[] = ['RecommendedArtist', 'MissingAlbum', 'RecommendedLibraryArtist', 'SeedLibraryArtist']

const newSeed = () => Math.floor(Math.random() * 1_000_000_000)

// How a row was marked this view (👍 / 👎 / 💤) so it stays in place until the next natural refresh.
type RowMark = Verdict | 'snoozed'
const markChip = (mark: RowMark) =>
  mark === 'up' ? '👍 Added' : mark === 'down' ? '👎 Dismissed' : '💤 Snoozed'

// An in-place decision marker with an undo: "👍 Added · undo". Every decision (like / dislike /
// snooze, on artists or albums) is reversible from the feed so a misclick is one click to fix.
function DecisionMark({ mark, onUndo, disabled }: { mark: RowMark; onUndo: () => void; disabled: boolean }) {
  return (
    <span className="disc-rated">
      {markChip(mark)}
      <button className="disc-undo" title="Undo this decision" disabled={disabled} onClick={onUndo}>
        undo
      </button>
    </span>
  )
}

const SNOOZE_OPTIONS: { label: string; duration: SnoozeDuration }[] = [
  { label: 'Week', duration: 'week' },
  { label: 'Month', duration: 'month' },
  { label: 'Year', duration: 'year' },
]

// The 💤 snooze action. The three durations (Week / Month / Year) are shown inline as direct
// buttons — no popover — so a "not now, remind me later" is a single click.
function SnoozeControl({
  onPick,
  disabled,
}: {
  onPick: (duration: SnoozeDuration) => void
  disabled: boolean
}) {
  return (
    <span className="disc-snooze" title="Snooze — hide for a while, then resurface">
      <span className="disc-snooze-label">💤</span>
      {SNOOZE_OPTIONS.map((o) => (
        <button
          key={o.duration}
          className="disc-btn snooze"
          disabled={disabled}
          onClick={() => onPick(o.duration)}
        >
          {o.label}
        </button>
      ))}
    </span>
  )
}

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

// Inline under a just-liked brand-new artist: their Deezer albums, each thumbable so a fresh
// discovery can actually be acquired (a liked album flows to the downloader). Reuses the parent's
// `rated`/play/rate plumbing so album rows mark in place exactly like top-level cards.
function ArtistAlbumsPanel({
  artist,
  rated,
  playing,
  togglePlay,
  onRate,
  onUndo,
  disabled,
}: {
  artist: string
  rated: Map<string, RowMark>
  playing: Set<string>
  togglePlay: (key: string) => void
  onRate: (item: FeedItem, verdict: Verdict) => void
  onUndo: (item: FeedItem) => void
  disabled: boolean
}) {
  const { data, isPending, isError } = useQuery({
    queryKey: ['artist-albums', artist],
    queryFn: () => getArtistAlbums(artist),
    staleTime: 5 * 60 * 1000,
  })

  if (isPending) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">Finding albums…</em></div>
  }
  if (isError || !data || data.length === 0) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">No albums found on Deezer.</em></div>
  }

  return (
    <div className="disc-sub-albums">
      <div className="disc-sub-note">Albums by {artist} — 👍 the ones to grab:</div>
      {data.map((album) => {
        const rowKey = `${album.artist.artistName}::${album.album}`
        const verdict = rated.get(rowKey)
        const isPlaying = playing.has(rowKey)
        return (
          <div className="disc-sub-album-wrap" key={rowKey}>
            <div className="disc-sub-album">
              <FeedAvatar item={album} size={36} />
              <div className="disc-sub-album-name">{album.album}</div>
              <div className="disc-actions">
                {album.deezerAlbumId && (
                  <button
                    className={isPlaying ? 'disc-btn play active' : 'disc-btn play'}
                    title={isPlaying ? 'Hide Deezer player' : 'Listen on Deezer'}
                    onClick={() => togglePlay(rowKey)}
                  >
                    {isPlaying ? '▾' : '▶'}
                  </button>
                )}
                {verdict ? (
                  <DecisionMark mark={verdict} disabled={disabled} onUndo={() => onUndo(album)} />
                ) : (
                  <>
                    <button className="disc-btn up" title="Queue album to buy" disabled={disabled} onClick={() => onRate(album, 'up')}>
                      👍
                    </button>
                    <button className="disc-btn down" title="Not interested" disabled={disabled} onClick={() => onRate(album, 'down')}>
                      👎
                    </button>
                  </>
                )}
              </div>
            </div>
            {isPlaying && album.deezerAlbumId && <DeezerSample albumId={album.deezerAlbumId} />}
          </div>
        )
      })}
    </div>
  )
}

// View state kept at module scope so navigating away from /discover and back restores the same feed
// instead of remounting fresh — which regenerated `seed` (reshuffling the whole list) and dropped the
// rated marks and a just-approved artist's inline albums. The QueryClient already caches the feed
// data across navigation; this keeps the local view in sync with it. Lives for the browser session
// (resets on full reload), which is the right scope for a randomized, react-to-it-now feed.
type DiscoverState = {
  shown: Set<FeedKind>
  page: number
  seed: number
  playing: Set<string>
  rated: Map<string, RowMark>
  expandedAlbums: Set<string>
}
const persisted: DiscoverState = {
  shown: new Set<FeedKind>(DEFAULT_KINDS),
  page: 0,
  seed: newSeed(),
  playing: new Set<string>(),
  rated: new Map<string, RowMark>(),
  expandedAlbums: new Set<string>(),
}

export default function Discover() {
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [shown, setShown] = useState<Set<FeedKind>>(() => persisted.shown)
  const [page, setPage] = useState(() => persisted.page)
  const [seed, setSeed] = useState(() => persisted.seed)
  // Artists whose Deezer player is expanded (kept collapsed by default so we don't mount many iframes).
  const [playing, setPlaying] = useState<Set<string>>(() => persisted.playing)
  // Rows rated this view, by row key -> verdict. They stay in place (marked, not removed) until the
  // next natural refresh, so a 👍/👎 doesn't reflow the whole list out from under you.
  const [rated, setRated] = useState<Map<string, RowMark>>(() => persisted.rated)
  // Brand-new artists liked this view: their albums are fetched and surfaced inline beneath the card
  // so the discovery can be acquired. Driven off state (not the feed item) so it survives the
  // in-place "rated" mark; cleared on the next natural refresh alongside `rated`.
  const [expandedAlbums, setExpandedAlbums] = useState<Set<string>>(() => persisted.expandedAlbums)

  // Mirror the live view state back into the module store every render so a later remount restores it.
  useEffect(() => {
    persisted.shown = shown
    persisted.page = page
    persisted.seed = seed
    persisted.playing = playing
    persisted.rated = rated
    persisted.expandedAlbums = expandedAlbums
  })

  // Keep a stable, sorted kinds list so the query key (and the server's interleave) are deterministic.
  const kinds = ALL_KINDS.filter((k) => shown.has(k))

  // A natural refresh (page, shuffle, or category change refetches the feed) clears the in-place
  // marks so the freshly-fetched list — which already excludes the rated items — starts clean.
  // The guard is value-based, not run-count-based: it clears only when the page/seed/kinds actually
  // change from what's currently shown. On a remount we restore the persisted state for this same
  // page/seed, so the key matches and nothing clears — and it stays correct under StrictMode, which
  // double-invokes the mount effect (a run-count "skip first mount" guard would clear on the 2nd run).
  const kindsKey = kinds.join(',')
  const lastRefreshKey = useRef(`${page}|${seed}|${kindsKey}`)
  useEffect(() => {
    const key = `${page}|${seed}|${kindsKey}`
    if (lastRefreshKey.current === key) return
    lastRefreshKey.current = key
    setRated(new Map())
    setExpandedAlbums(new Set())
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
    // Freeze the feed for the session. Without this it defaults to stale-immediately and refetches on
    // every remount (i.e. navigating away and back) — and because the server drops just-rated artists
    // from the feed, that refetch made an approved artist's card (and its inline albums) disappear.
    // A new seed/page is a different query key, so Shuffle and paging still fetch fresh; Rebuild
    // invalidates ['feed'] explicitly; a full page reload starts a new session with a new seed.
    staleTime: Infinity,
    gcTime: 60 * 60 * 1000,
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
    onSuccess: (_data, { item, verdict }) => {
      queryClient.invalidateQueries({ queryKey: ['purchases'] })
      queryClient.invalidateQueries({ queryKey: ['ratings'] })
      // Liking a brand-new artist surfaces their albums inline so the find can be acquired.
      if (verdict === 'up' && item.kind === 'RecommendedArtist') {
        setExpandedAlbums((prev) => new Set(prev).add(item.artist.artistName))
      }
    },
  })
  const snoozeMutation = useMutation({
    mutationFn: ({ item, duration }: { item: FeedItem; duration: SnoozeDuration }) => snooze(item, duration),
    // Mark in place like a rating; snooze writes a decided row so the artist drops out of the feed on
    // the next natural refresh. Doesn't touch the buy list (a snooze isn't a "yes").
    onMutate: ({ item }) => {
      setRated((prev) => new Map(prev).set(rowKeyFor(item), 'snoozed'))
    },
    onError: (_err, { item }) => {
      setRated((prev) => {
        const next = new Map(prev)
        next.delete(rowKeyFor(item))
        return next
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ratings'] })
    },
  })
  // The inline album decisions made under a just-liked brand-new artist (from its expanded panel),
  // read from the react-query cache. Used so undoing the artist also walks back those album picks.
  const decidedAlbumsFor = (artistName: string): FeedItem[] => {
    const albums = queryClient.getQueryData<FeedItem[]>(['artist-albums', artistName]) ?? []
    return albums.filter((a) => rated.has(rowKeyFor(a)))
  }

  // Undo any decision (like / dislike / snooze, artist or album), clearing it back to actionable.
  // Optimistically drops the in-place mark so the card's 👍/👎/💤 reappear instantly; rolls back on
  // failure. Undoing a recommended artist also clears the album decisions made in its inline panel
  // (and collapses it) — you went back on the artist, so its album picks shouldn't linger.
  const undo = useMutation({
    mutationFn: async (item: FeedItem) => {
      await clearRating(item)
      if (item.kind === 'RecommendedArtist') {
        await Promise.all(decidedAlbumsFor(item.artist.artistName).map((a) => clearRating(a)))
      }
    },
    onMutate: (item) => {
      const prev = new Map(rated)
      const next = new Map(rated)
      next.delete(rowKeyFor(item))
      if (item.kind === 'RecommendedArtist') {
        decidedAlbumsFor(item.artist.artistName).forEach((a) => next.delete(rowKeyFor(a)))
        setExpandedAlbums((p) => {
          const n = new Set(p)
          n.delete(item.artist.artistName)
          return n
        })
      }
      setRated(next)
      return { prev }
    },
    onError: (_err, _item, ctx) => {
      if (ctx?.prev) setRated(ctx.prev)
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['ratings'] })
      queryClient.invalidateQueries({ queryKey: ['purchases'] })
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

  const busy = rateMutation.isPending || snoozeMutation.isPending || undo.isPending || rebuild.isPending
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
              <label key={c.kind} className="feed-filter" title={c.tip}>
                <input type="checkbox" checked={shown.has(c.kind)} onChange={() => toggleCategory(c.kind)} />
                {c.label}
              </label>
            ))}
          </div>
        </div>
        {STANDALONE_FILTERS.map((c) => (
          <label key={c.kind} className="feed-filter" title={c.tip}>
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
      {snoozeMutation.isError && <p className="error">Snooze failed: {(snoozeMutation.error as Error).message}</p>}
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
                      <DecisionMark mark={verdict} disabled={busy} onUndo={() => undo.mutate(item)} />
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
                        {/* Snooze hides a "not now" pick for a while — works for artists and missing albums. */}
                        <SnoozeControl
                          onPick={(duration) => snoozeMutation.mutate({ item, duration })}
                          disabled={rebuild.isPending}
                        />
                      </>
                    )}
                  </div>
                </div>
                {isPlaying && (isAlbum
                  ? <DeezerSample albumId={item.deezerAlbumId!} />
                  : <DeezerSample artist={name} />)}
                {!isAlbum && expandedAlbums.has(name) && (
                  <ArtistAlbumsPanel
                    artist={name}
                    rated={rated}
                    playing={playing}
                    togglePlay={togglePlay}
                    onRate={(albumItem, v) => rateMutation.mutate({ item: albumItem, verdict: v })}
                    onUndo={(albumItem) => undo.mutate(albumItem)}
                    disabled={rebuild.isPending}
                  />
                )}
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
