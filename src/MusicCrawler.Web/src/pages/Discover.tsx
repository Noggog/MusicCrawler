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
import { IconApprove, IconMoon, IconReject } from '../components/icons'

const PAGE_SIZE = 20

// The badge shown on each card so it's obvious what action a card is asking for, keyed by kind.
const BADGE: Record<FeedKind, string> = {
  RecommendedArtist: 'Recommended New Artist',
  MissingAlbum: 'Add missing album',
  RecommendedLibraryArtist: 'Recommended Artist',
  SeedLibraryArtist: 'Rate Unfamiliar Artist',
  LibraryArtist: 'Mark existing artist',
}

// The category filters, shown up top as toggle-able tag chips styled exactly like the per-row
// badges — clicking a chip shows/hides that kind in the feed. Order mirrors how they read on a card.
const FILTER_CHIPS: { kind: FeedKind; tip: string }[] = [
  { kind: 'RecommendedArtist', tip: 'Artists not yet in the library' },
  { kind: 'RecommendedLibraryArtist', tip: "Unrated artists already in the library" },
  { kind: 'SeedLibraryArtist', tip: "Rate artists not yet recommended to grow the frontier" },
  { kind: 'MissingAlbum', tip: 'Missing albums for artists you like' },
]

const ALL_KINDS: FeedKind[] = ['RecommendedArtist', 'MissingAlbum', 'RecommendedLibraryArtist', 'SeedLibraryArtist']
// Default to everything on: the recommended sections (new + existing owned), missing albums, and the
// seed section (owned artists nothing recommends yet) — rating those grows the frontier.
const DEFAULT_KINDS: FeedKind[] = ['RecommendedArtist', 'MissingAlbum', 'RecommendedLibraryArtist', 'SeedLibraryArtist']

const newSeed = () => Math.floor(Math.random() * 1_000_000_000)

// How a row was marked this view (approve / reject / snooze) so it stays in place until the next
// natural refresh.
type RowMark = Verdict | 'snoozed'
const MARK_LABEL: Record<RowMark, string> = { up: 'Added', down: 'Dismissed', snoozed: 'Snoozed' }
const MarkIcon = ({ mark }: { mark: RowMark }) =>
  mark === 'up' ? <IconApprove size={15} /> : mark === 'down' ? <IconReject size={15} /> : <IconMoon size={15} />

// An in-place decision marker with an undo: "✓ Added · undo". Every decision (approve / reject /
// snooze, on artists or albums) is reversible from the feed so a misclick is one click to fix.
function DecisionMark({ mark, onUndo, disabled }: { mark: RowMark; onUndo: () => void; disabled: boolean }) {
  return (
    <span className={`disc-rated mark-${mark}`}>
      <span className="disc-rated-icon"><MarkIcon mark={mark} /></span>
      {MARK_LABEL[mark]}
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
const SNOOZE_LABEL: Record<SnoozeDuration, string> = {
  week: 'a week',
  month: 'a month',
  year: 'a year',
}

// "Sticky snooze": remember the last duration the user picked so a quick click on the moon re-applies
// it without reopening the flyout. Persisted in localStorage and mirrored across every SnoozeControl
// on the page via a custom event (the native `storage` event only fires in *other* tabs).
const SNOOZE_PREF_KEY = 'mc.snooze.last'
const SNOOZE_PREF_EVENT = 'mc-snooze-changed'
const DEFAULT_SNOOZE: SnoozeDuration = 'week'

function readStickySnooze(): SnoozeDuration {
  try {
    const stored = localStorage.getItem(SNOOZE_PREF_KEY)
    if (stored && stored in SNOOZE_LABEL) return stored as SnoozeDuration
  } catch {
    // localStorage can throw (private mode / disabled storage) — fall back to the default.
  }
  return DEFAULT_SNOOZE
}

function useStickySnooze(): [SnoozeDuration, (duration: SnoozeDuration) => void] {
  const [duration, setDuration] = useState<SnoozeDuration>(readStickySnooze)
  useEffect(() => {
    const sync = () => setDuration(readStickySnooze())
    window.addEventListener(SNOOZE_PREF_EVENT, sync)
    window.addEventListener('storage', sync)
    return () => {
      window.removeEventListener(SNOOZE_PREF_EVENT, sync)
      window.removeEventListener('storage', sync)
    }
  }, [])
  const remember = (next: SnoozeDuration) => {
    try {
      localStorage.setItem(SNOOZE_PREF_KEY, next)
    } catch {
      // Ignore — we still update in-memory state so this session behaves correctly.
    }
    setDuration(next)
    window.dispatchEvent(new Event(SNOOZE_PREF_EVENT))
  }
  return [duration, remember]
}

// The 💤 snooze action. Collapsed to just the icon; hovering (or focusing) grows it rightward into a
// matching dark-glass square holding the three durations (Week / Month / Year), absolutely positioned
// so it overflows the row without resizing it or nudging the thumbs up/down beside it. The spans run
// a "frozen" scale — purple Week → icy Year (see data-duration styling in index.css).
//
// Clicking the moon itself re-applies the last-used duration ("sticky snooze") — the panel is only
// needed when you want a *different* span. Picking from the panel updates that remembered default.
function SnoozeControl({
  onPick,
  disabled,
}: {
  onPick: (duration: SnoozeDuration) => void
  disabled: boolean
}) {
  const [lastDuration, rememberDuration] = useStickySnooze()
  const pick = (duration: SnoozeDuration) => {
    rememberDuration(duration)
    onPick(duration)
  }
  return (
    <span className="disc-snooze" title="Snooze — hide for a while, then resurface">
      <button
        type="button"
        className="disc-btn snooze snooze-trigger"
        disabled={disabled}
        aria-haspopup="menu"
        aria-label={`Snooze for ${SNOOZE_LABEL[lastDuration]}`}
        title={`Snooze for ${SNOOZE_LABEL[lastDuration]} — hover for other spans`}
        onClick={() => pick(lastDuration)}
      >
        <IconMoon size={18} />
      </button>
      <span className="disc-snooze-flyout" role="menu">
        {SNOOZE_OPTIONS.map((o) => (
          <button
            key={o.duration}
            type="button"
            className={`disc-btn snooze${o.duration === lastDuration ? ' is-last' : ''}`}
            data-duration={o.duration}
            role="menuitemradio"
            aria-checked={o.duration === lastDuration}
            disabled={disabled}
            onClick={() => pick(o.duration)}
          >
            {o.label}
          </button>
        ))}
      </span>
    </span>
  )
}

// Stable identity for a feed row, shared by render and the rate mutation so a rated row can be
// marked in place. Albums key on artist+album; artists on kind+name.
const rowKeyFor = (item: FeedItem) =>
  item.album ? `${item.artist.artistName}::${item.album}` : `${item.kind}:${item.artist.artistName}`

// The image a source supplied (artist photo or album art), or a coloured initial when missing.
// `hero` renders the large readout image: drop the inline size so CSS (.detail-hero) drives it.
function FeedAvatar({ item, size, hero }: { item: FeedItem; size: number; hero?: boolean }) {
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
    return <img className="disc-avatar" src={src} alt={label} width={hero ? undefined : size} height={hero ? undefined : size} />
  }
  return (
    <div
      className="disc-avatar disc-avatar-fallback"
      style={hero ? undefined : { width: size, height: size, fontSize: size / 2.5 }}
    >
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
      <div className="disc-sub-note">Albums by {artist} — approve the ones to grab:</div>
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
                      <IconApprove />
                    </button>
                    <button className="disc-btn down" title="Not interested" disabled={disabled} onClick={() => onRate(album, 'down')}>
                      <IconReject />
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

// The right-hand readout (desktop) / bottom drawer (mobile) for the row currently selected in the
// list. A big hero image, recommendation chips, a Deezer preview player, and — for a brand-new
// recommended artist — their grabbable albums. All the rate/snooze/undo plumbing is threaded in from
// the parent so a decision made here marks the matching list row in place too.
function DetailPanel({
  item,
  rated,
  busy,
  rebuildPending,
  playing,
  togglePlay,
  onRate,
  onSnooze,
  onUndo,
  onClose,
}: {
  item: FeedItem | null
  rated: Map<string, RowMark>
  busy: boolean
  rebuildPending: boolean
  playing: Set<string>
  togglePlay: (key: string) => void
  onRate: (item: FeedItem, verdict: Verdict) => void
  onSnooze: (item: FeedItem, duration: SnoozeDuration) => void
  onUndo: (item: FeedItem) => void
  onClose: () => void
}) {
  if (!item) {
    return (
      <aside className="disc-detail is-empty">
        <div className="disc-detail-empty">
          <span className="detail-empty-icon">🎧</span>
        </div>
      </aside>
    )
  }

  const name = item.artist.artistName
  const isAlbum = !!item.album
  const rowKey = rowKeyFor(item)
  const verdict = rated.get(rowKey)
  const canPlay = isAlbum ? !!item.deezerAlbumId : true

  return (
    <aside className="disc-detail">
      <button className="detail-close" title="Close" onClick={onClose}>✕</button>

      {/* Header: art aligned left with the badge / title / chips / actions stacked to its right, so the
          square no longer floats alone in a sea of empty space. Tracks + albums stay full-width below. */}
      <div className="detail-header">
        <div className="detail-hero">
          <FeedAvatar item={item} size={0} hero />
        </div>

        <div className="detail-headinfo">
          <span className={`feed-badge feed-badge-${item.kind}`}>{BADGE[item.kind]}</span>
          <h2 className="detail-name">{item.album ?? name}</h2>

          {isAlbum ? (
            <div className="detail-chips">
              <span className="detail-chip">Album</span>
              <span className="detail-chip via">{name}</span>
            </div>
          ) : item.sources.length > 0 ? (
            <>
              <div className="detail-section-label">Recommended via</div>
              <div className="detail-chips">
                {item.sources.slice(0, 8).map((s) => (
                  <span className="detail-chip via" key={s}>{s}</span>
                ))}
              </div>
            </>
          ) : null}

          <div className="detail-actions">
            {verdict ? (
              <DecisionMark mark={verdict} disabled={busy} onUndo={() => onUndo(item)} />
            ) : (
              <>
                <button
                  className="disc-btn up"
                  title={isAlbum ? 'Queue album to buy' : 'Approve'}
                  disabled={rebuildPending}
                  onClick={() => onRate(item, 'up')}
                >
                  <IconApprove />
                </button>
                <button
                  className="disc-btn down"
                  title="Not interested"
                  disabled={rebuildPending}
                  onClick={() => onRate(item, 'down')}
                >
                  <IconReject />
                </button>
                <SnoozeControl onPick={(duration) => onSnooze(item, duration)} disabled={rebuildPending} />
              </>
            )}
          </div>
        </div>
      </div>

      {canPlay && (
        <>
          {/* DeezerSample renders its own "Album tracks" / "Top tracks" header (with the Deezer link),
              so no separate detail-section-label here — that produced a duplicate heading. */}
          {/* Key by row so switching selection remounts the player (stops the previous preview). */}
          {isAlbum ? (
            <DeezerSample key={rowKey} albumId={item.deezerAlbumId!} />
          ) : (
            <DeezerSample key={rowKey} artist={name} />
          )}
        </>
      )}

      {/* A brand-new recommended artist: show their acquirable albums so a find can be grabbed. */}
      {item.kind === 'RecommendedArtist' && (
        <>
          <div className="detail-section-label">Albums</div>
          <ArtistAlbumsPanel
            artist={name}
            rated={rated}
            playing={playing}
            togglePlay={togglePlay}
            onRate={onRate}
            onUndo={onUndo}
            disabled={rebuildPending}
          />
        </>
      )}
    </aside>
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
  // The feed row open in the readout panel (desktop) / bottom drawer (mobile).
  selected: FeedItem | null
}
const persisted: DiscoverState = {
  shown: new Set<FeedKind>(DEFAULT_KINDS),
  page: 0,
  seed: newSeed(),
  playing: new Set<string>(),
  rated: new Map<string, RowMark>(),
  selected: null,
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
  // The row whose readout is open on the right (desktop) / in the drawer (mobile). Liking a brand-new
  // recommended artist auto-selects it so its grabbable albums surface in the panel.
  const [selected, setSelected] = useState<FeedItem | null>(() => persisted.selected)

  // Mirror the live view state back into the module store every render so a later remount restores it.
  useEffect(() => {
    persisted.shown = shown
    persisted.page = page
    persisted.seed = seed
    persisted.playing = playing
    persisted.rated = rated
    persisted.selected = selected
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
    // The selected item has almost certainly left the freshly-fetched feed — close the readout.
    setSelected(null)
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
      // Liking a brand-new artist opens its readout so the albums-to-grab panel surfaces.
      if (verdict === 'up' && item.kind === 'RecommendedArtist') {
        setSelected(item)
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
  // failure. Undoing a recommended artist also clears the album decisions made in its readout panel —
  // you went back on the artist, so its album picks shouldn't linger.
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
  // Identity of the row open in the readout, so the matching list row renders as selected.
  const selectedKey = selected ? rowKeyFor(selected) : null

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

      {/* The category tags double as the filter: click a chip to show/hide that kind in the feed. */}
      <div className="feed-filters">
        {FILTER_CHIPS.map(({ kind, tip }) => {
          const on = shown.has(kind)
          return (
            <button
              key={kind}
              type="button"
              title={tip}
              aria-pressed={on}
              className={`feed-chip feed-badge feed-badge-${kind}${on ? '' : ' off'}`}
              onClick={() => toggleCategory(kind)}
            >
              {BADGE[kind]}
            </button>
          )
        })}
      </div>

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
        <div className="disc-layout">
          <div className="disc-main">
            <div className={isFetching ? 'disc-list fetching' : 'disc-list'}>
              {items.map((item) => {
                const name = item.artist.artistName
                const isAlbum = !!item.album
                const rowKey = isAlbum ? `${name}::${item.album}` : `${item.kind}:${name}`
                const verdict = rated.get(rowKey)
                const isSelected = selectedKey === rowKey
                return (
                  <div className={verdict ? 'disc-row-wrap rated' : 'disc-row-wrap'} key={rowKey}>
                    {/* The whole row opens the readout; the action cluster stops the click so a
                        thumb/snooze doesn't also yank the panel open. */}
                    <div
                      className={isSelected ? 'disc-row selected' : 'disc-row'}
                      onClick={() => setSelected(item)}
                    >
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
                      <div className="disc-actions" onClick={(e) => e.stopPropagation()}>
                        {verdict ? (
                          <DecisionMark mark={verdict} disabled={busy} onUndo={() => undo.mutate(item)} />
                        ) : (
                          <>
                            <button
                              className="disc-btn up"
                              title={isAlbum ? 'Queue album to buy' : 'Approve'}
                              disabled={rebuild.isPending}
                              onClick={() => rateMutation.mutate({ item, verdict: 'up' })}
                            >
                              <IconApprove />
                            </button>
                            <button
                              className="disc-btn down"
                              title="Not interested"
                              disabled={rebuild.isPending}
                              onClick={() => rateMutation.mutate({ item, verdict: 'down' })}
                            >
                              <IconReject />
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
                  </div>
                )
              })}
            </div>

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
          </div>

          <DetailPanel
            item={selected}
            rated={rated}
            busy={busy}
            rebuildPending={rebuild.isPending}
            playing={playing}
            togglePlay={togglePlay}
            onRate={(item, verdict) => rateMutation.mutate({ item, verdict })}
            onSnooze={(item, duration) => snoozeMutation.mutate({ item, duration })}
            onUndo={(item) => undo.mutate(item)}
            onClose={() => setSelected(null)}
          />
        </div>
      )}
    </section>
  )
}
