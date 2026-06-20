import { Fragment, useState, type CSSProperties } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { clearDeezerId, getArtists, refreshCatalog, resolveAllDeezer, setDeezerId } from '../api/artists'
import { searchDeezerArtists } from '../api/deezer'
import { clearRating, getArtistDiscography, getRatings, rate, type Verdict } from '../api/discovery'
import { useArtAccent } from '../art/artColors'
import { rateFeedback } from '../effects/effectsBus'
import type { ArtistAlbumItem, ArtistListItem, DeezerCandidate, DiscoveryStatus, FeedItem } from '../types'
import { useAuth } from '../auth/AuthContext'
import { IconApprove, IconCheck, IconClear, IconReject, IconWrench } from '../components/icons'

const verdictStatus = (v: Verdict): DiscoveryStatus => (v === 'up' ? 'Liked' : 'Disliked')

// The full library loads in one fetch, but rendering every row at once is the costly part — each
// row extracts an accent colour from its photo — so we page the rendered rows. Search still spans
// the whole library (it filters before paging).
const PAGE_SIZE = 50

const normalize = (s: string) => s.trim().toLowerCase()

// A library row is "suspect" when it resolved to a Deezer artist whose name doesn't match — the
// tell-tale of a misassociation (e.g. library "ALEX" → Deezer "Alex Warren").
const isSuspect = (a: ArtistListItem) =>
  a.deezerId != null && a.deezerName != null && normalize(a.deezerName) !== normalize(a.artistKey.artistName)

function formatFans(n: number | null): string {
  if (n == null) return ''
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(n >= 10_000 ? 0 : 1)}k`
  return String(n)
}

// Inline picker to pin the correct Deezer artist for a library artist. Searches Deezer (prefilled
// with the artist's name) and lets the user pick the right candidate. We surface each candidate's
// photo, fan count and a Deezer ↗ link rather than an audio preview, because the preview player
// resolves by name — the very thing that's wrong here — so the link is the reliable way to verify.
function CorrectPicker({
  artist,
  onClose,
  onApplied,
}: {
  artist: ArtistListItem
  onClose: () => void
  onApplied: () => void
}) {
  const name = artist.artistKey.artistName
  const [query, setQuery] = useState(name)

  const search = useQuery({
    queryKey: ['deezer-search', query],
    queryFn: () => searchDeezerArtists(query),
    enabled: query.trim().length > 0,
  })

  const apply = useMutation({
    mutationFn: (id: number) => setDeezerId(name, id),
    onSuccess: onApplied,
  })

  const reset = useMutation({
    mutationFn: () => clearDeezerId(name),
    onSuccess: onApplied,
  })

  return (
    <div className="picker-backdrop" onClick={onClose}>
      <div className="picker-panel" onClick={(e) => e.stopPropagation()}>
        <div className="picker-head">
          <h2>Correct “{name}”</h2>
          <button className="auth-btn" onClick={onClose}>Close</button>
        </div>
        <p>
          <em>Pick the right Deezer artist — use the ↗ link to confirm before applying.</em>
        </p>

        <input
          className="picker-search"
          type="text"
          value={query}
          autoFocus
          placeholder="Search Deezer…"
          onChange={(e) => setQuery(e.target.value)}
        />

        {artist.deezerOverride && (
          <p className="picker-pinned">
            Currently pinned to Deezer #{artist.deezerId}.{' '}
            <button className="link-btn" onClick={() => reset.mutate()} disabled={reset.isPending}>
              Reset to automatic
            </button>
          </p>
        )}

        {search.isPending && query.trim() && <p><em>Searching…</em></p>}
        {search.isError && <p className="error">Search failed.</p>}

        <ul className="picker-results">
          {(search.data ?? []).map((c: DeezerCandidate) => {
            const current = c.id === artist.deezerId
            return (
              <li key={c.id} className={current ? 'picker-result current' : 'picker-result'}>
                {c.imageUrl ? (
                  <img className="picker-thumb" src={c.imageUrl} alt="" />
                ) : (
                  <div className="picker-thumb placeholder" />
                )}
                <div className="picker-meta">
                  <span className="picker-name">{c.name ?? '(unknown)'}</span>
                  <span className="picker-sub">
                    {formatFans(c.fans)} fans · #{c.id}
                    {current && ' · current'}
                  </span>
                </div>
                <a
                  className="deezer-link"
                  href={c.link ?? `https://www.deezer.com/artist/${c.id}`}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Deezer ↗
                </a>
                <button
                  className="auth-btn"
                  disabled={apply.isPending || current}
                  onClick={() => apply.mutate(c.id)}
                >
                  {current ? 'In use' : 'Use this'}
                </button>
              </li>
            )
          })}
          {search.data && search.data.length === 0 && query.trim() && (
            <li><em>No Deezer matches.</em></li>
          )}
        </ul>

        {apply.isError && <p className="error">Failed to apply: {(apply.error as Error).message}</p>}
      </div>
    </div>
  )
}

// Album art (or a coloured initial) for an album in the discography drill-down.
function AlbumThumb({ item }: { item: ArtistAlbumItem }) {
  if (item.imageUrl) {
    return <img className="disc-avatar" src={item.imageUrl} alt="" width={36} height={36} loading="lazy" />
  }
  return (
    <div className="disc-avatar disc-avatar-fallback" style={{ width: 36, height: 36, fontSize: 15 }}>
      {item.album.charAt(0).toUpperCase()}
    </div>
  )
}

// A decided missing album (queued / dismissed / snoozed) with a one-click clear back to actionable.
function AlbumState({ label, onClear, busy }: { label: string; onClear: () => void; busy: boolean }) {
  return (
    <span className="album-state">
      {label}
      <button className="disc-btn" title="Clear — return to choices" disabled={busy} onClick={onClear}>
        <IconClear size={15} />
      </button>
    </span>
  )
}

// Verdict → the label shown on a decided missing album.
const ALBUM_VERDICT_LABEL: Partial<Record<DiscoveryStatus, string>> = {
  Liked: 'Queued',
  Disliked: 'Dismissed',
  Snoozed: 'Snoozed',
}

// A single album in the discography drill-down, themed from its cover art via `--art-accent` (the
// shared `.disc-sub-album` styling turns that into the tinted card + the cover's glow).
function AlbumSubRow({
  a,
  busy,
  onRate,
  onClear,
}: {
  a: ArtistAlbumItem
  busy: boolean
  onRate: (a: ArtistAlbumItem, verdict: Verdict) => void
  onClear: (a: ArtistAlbumItem) => void
}) {
  const accent = useArtAccent(a.imageUrl)
  const accentStyle = accent ? ({ '--art-accent': accent } as CSSProperties) : undefined
  const label = a.verdict ? ALBUM_VERDICT_LABEL[a.verdict] : null
  return (
    <div className="disc-sub-album-wrap">
      <div className={`disc-sub-album no-play${a.owned ? ' owned' : ''}`} style={accentStyle}>
        <AlbumThumb item={a} />
        <div className="disc-sub-album-name">{a.album}</div>
        <div className="disc-actions">
          {a.owned ? (
            <span className="album-owned" title="Already in your library">
              <IconCheck size={15} /> In library
            </span>
          ) : label ? (
            <AlbumState label={label} busy={busy} onClear={() => onClear(a)} />
          ) : (
            <>
              <button
                className="disc-btn up"
                title="Queue album to buy"
                disabled={busy}
                onClick={() => onRate(a, 'up')}
              >
                <IconApprove />
              </button>
              <button
                className="disc-btn down"
                title="Not interested"
                disabled={busy}
                onClick={() => onRate(a, 'down')}
              >
                <IconReject />
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

// The drill-down under an expanded artist: their full Deezer discography, owned albums flagged and
// missing ones thumbable so they can be queued to buy (or dismissed) right here — no trip through
// Discover. Fetched on demand (one Deezer call) only when the row is expanded.
function ArtistAlbums({ artist }: { artist: string }) {
  const queryClient = useQueryClient()
  const { data, isPending, isError } = useQuery({
    queryKey: ['artist-discography', artist],
    queryFn: () => getArtistDiscography(artist),
    staleTime: 5 * 60 * 1000,
  })

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['artist-discography', artist] })
    queryClient.invalidateQueries({ queryKey: ['purchases'] })
    queryClient.invalidateQueries({ queryKey: ['ratings'] })
  }

  // rate/clearRating only read artist/album/imageUrl/deezerAlbumId — build the minimal feed item.
  const toFeedItem = (a: ArtistAlbumItem): FeedItem => ({
    kind: 'MissingAlbum',
    artist: a.artist,
    album: a.album,
    imageUrl: a.imageUrl,
    score: 0,
    sources: [],
    deezerAlbumId: a.deezerAlbumId,
  })

  const rateAlbum = useMutation({
    mutationFn: ({ a, verdict }: { a: ArtistAlbumItem; verdict: Verdict }) => rate(toFeedItem(a), verdict),
    onMutate: ({ verdict }) => rateFeedback(verdict),
    onSuccess: invalidate,
  })
  const clearAlbum = useMutation({
    mutationFn: (a: ArtistAlbumItem) => clearRating(toFeedItem(a)),
    onSuccess: invalidate,
  })
  const busy = rateAlbum.isPending || clearAlbum.isPending

  if (isPending) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">Loading albums…</em></div>
  }
  if (isError || !data) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">Couldn’t load albums.</em></div>
  }
  if (data.length === 0) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">No albums found on Deezer.</em></div>
  }

  return (
    <div className="disc-sub-albums">
      {data.map((a) => (
        <AlbumSubRow
          key={a.album}
          a={a}
          busy={busy}
          onRate={(album, verdict) => rateAlbum.mutate({ a: album, verdict })}
          onClear={(album) => clearAlbum.mutate(album)}
        />
      ))}
    </div>
  )
}

// One artist in the table, themed from its photo via `--art-accent` (see index.css `.artist-row`) so
// the row + thumbnail glow in the artist's own colour, matching the Discover feed / Download queue.
function ArtistRow({
  artist,
  verdict,
  isOpen,
  user,
  ratePending,
  onRate,
  onToggle,
  onCorrect,
}: {
  artist: ArtistListItem
  verdict: DiscoveryStatus | undefined
  isOpen: boolean
  user: boolean
  ratePending: boolean
  onRate: (name: string, verdict: Verdict, current?: DiscoveryStatus) => void
  onToggle: (name: string) => void
  onCorrect: (artist: ArtistListItem) => void
}) {
  const name = artist.artistKey.artistName
  const suspect = isSuspect(artist)
  const accent = useArtAccent(artist.artistImageUrl)
  const accentStyle = accent ? ({ '--art-accent': accent } as CSSProperties) : undefined
  return (
    <Fragment>
      <tr
        className={user ? (isOpen ? 'artist-row clickable open' : 'artist-row clickable') : 'artist-row'}
        style={accentStyle}
        onClick={user ? () => onToggle(name) : undefined}
      >
        {user && (
          <td onClick={(e) => e.stopPropagation()}>
            <div className="rate-cell">
              <button
                className={verdict === 'Liked' ? 'disc-btn up active' : 'disc-btn up'}
                title={verdict === 'Liked' ? 'Clear rating' : 'Approve'}
                disabled={ratePending}
                onClick={() => onRate(name, 'up', verdict)}
              >
                <IconApprove />
              </button>
              <button
                className={verdict === 'Disliked' ? 'disc-btn down active' : 'disc-btn down'}
                title={verdict === 'Disliked' ? 'Clear rating' : 'Reject'}
                disabled={ratePending}
                onClick={() => onRate(name, 'down', verdict)}
              >
                <IconReject />
              </button>
            </div>
          </td>
        )}
        <td>
          <div className="artist-name-cell">
            {artist.artistImageUrl ? (
              <img className="artist-thumb" src={artist.artistImageUrl} alt="" loading="lazy" />
            ) : (
              <div className="artist-thumb placeholder">{name.charAt(0).toUpperCase()}</div>
            )}
            <div className="artist-name-main">
              <div className="artist-name-row">
                {/* The name itself links out to Deezer once resolved; plain text until then. */}
                {artist.deezerId == null ? (
                  <span>{name}</span>
                ) : (
                  <a
                    className="artist-name-link"
                    href={artist.deezerLink ?? `https://www.deezer.com/artist/${artist.deezerId}`}
                    target="_blank"
                    rel="noopener noreferrer"
                    onClick={(e) => e.stopPropagation()}
                    title={
                      suspect && artist.deezerName
                        ? `Deezer: ${artist.deezerName} — likely the wrong artist`
                        : artist.deezerName ?? undefined
                    }
                  >
                    {name}
                  </a>
                )}
                {suspect && (
                  <span className="warn-badge" title="Deezer name doesn't match — likely the wrong artist">⚠</span>
                )}
              </div>
              {artist.genres.length > 0 && (
                <div className="genre-tags">
                  {artist.genres.slice(0, 3).map((g) => (
                    <span className="genre-tag" key={g}>{g}</span>
                  ))}
                </div>
              )}
            </div>
            {user && (
              <button
                className="wrench-btn"
                title="Correct the Deezer association"
                aria-label={`Correct the Deezer association for ${name}`}
                onClick={(e) => {
                  e.stopPropagation()
                  onCorrect(artist)
                }}
              >
                <IconWrench size={22} />
              </button>
            )}
          </div>
        </td>
      </tr>
      {user && isOpen && (
        <tr className="album-drill-row">
          <td colSpan={2}>
            <ArtistAlbums artist={name} />
          </td>
        </tr>
      )}
    </Fragment>
  )
}

export default function Artists() {
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(0)
  const [correcting, setCorrecting] = useState<ArtistListItem | null>(null)
  // The one artist whose album drill-down is expanded (by name) — one open at a time.
  const [expanded, setExpanded] = useState<string | null>(null)

  // Editing the search resets to the first page so matches are never hidden on a later page.
  const onSearch = (next: string) => {
    setQuery(next)
    setPage(0)
  }

  const toggleExpanded = (name: string) =>
    setExpanded((prev) => (prev === name ? null : name))

  const { data: artists, isPending, isError, error } = useQuery({
    queryKey: ['artists'],
    queryFn: getArtists,
  })

  // Ratings are per-user; fetch them only when signed in so we can show each band's verdict.
  const { data: ratings } = useQuery({
    queryKey: ['ratings'],
    queryFn: getRatings,
    enabled: !!user,
  })

  // artist name -> current verdict (artist ratings only; album verdicts live in the album drill-down).
  const verdictByArtist = new Map<string, DiscoveryStatus>()
  for (const r of ratings ?? []) {
    if (!r.album) verdictByArtist.set(r.artist.artistName, r.verdict)
  }

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: ['ratings'] })
    queryClient.invalidateQueries({ queryKey: ['feed'] })
    queryClient.invalidateQueries({ queryKey: ['purchases'] })
  }

  // Thumbing an artist. Clicking the verdict that's already set clears it back to neutral.
  const rateArtist = useMutation({
    mutationFn: ({ artist, verdict, current }: { artist: string; verdict: Verdict; current?: DiscoveryStatus }) => {
      const item: FeedItem = {
        kind: 'LibraryArtist',
        artist: { artistName: artist },
        album: null,
        imageUrl: null,
        score: 0,
        sources: [],
        deezerAlbumId: null,
      }
      return current === verdictStatus(verdict) ? clearRating(item) : rate(item, verdict)
    },
    // Same flare as Discover — but not when the click clears an existing verdict.
    onMutate: ({ verdict, current }) =>
      rateFeedback(current === verdictStatus(verdict) ? null : verdict),
    onSuccess: invalidate,
  })

  const refresh = useMutation({
    mutationFn: refreshCatalog,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['artists'] }),
  })

  const resolveAll = useMutation({
    mutationFn: resolveAllDeezer,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['artists'] }),
  })

  // After a correction is applied/reset, refresh the list (Deezer columns) and downstream feeds.
  const afterCorrection = () => {
    queryClient.invalidateQueries({ queryKey: ['artists'] })
    queryClient.invalidateQueries({ queryKey: ['feed'] })
    queryClient.invalidateQueries({ queryKey: ['related'] })
    setCorrecting(null)
  }

  const filtered = (artists ?? []).filter((a) =>
    normalize(a.artistKey.artistName).includes(normalize(query)),
  )

  // Clamp to a valid page after the filter shrinks (e.g. a search that lands past the current page).
  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const safePage = Math.min(page, pageCount - 1)
  const paged = filtered.slice(safePage * PAGE_SIZE, safePage * PAGE_SIZE + PAGE_SIZE)

  return (
    <section>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'center', gap: '1rem' }}>
        <h1>Artists</h1>
        {/* The catalog auto-syncs on startup and daily; these manual triggers are dev-only
            conveniences and are compiled out of production builds. */}
        {import.meta.env.DEV && (
          <button onClick={() => refresh.mutate()} disabled={refresh.isPending}>
            {refresh.isPending ? 'Refreshing…' : 'Refresh from Plex (dev)'}
          </button>
        )}
        {import.meta.env.DEV && user && (
          <button onClick={() => resolveAll.mutate()} disabled={resolveAll.isPending}>
            {resolveAll.isPending ? 'Resolving…' : 'Resolve Deezer for all (dev)'}
          </button>
        )}
      </div>

      {user && (
        <p>
          <em>Review your ratings</em>
        </p>
      )}

      {import.meta.env.DEV && refresh.isError && (
        <p className="error">Refresh failed: {(refresh.error as Error).message}</p>
      )}

      {import.meta.env.DEV && refresh.isSuccess && (
        <p>
          <em>
            Synced: {refresh.data.upserted} from Plex, {refresh.data.markedAbsent} removed,{' '}
            {refresh.data.totalPresent} in catalog.
          </em>
        </p>
      )}

      {import.meta.env.DEV && resolveAll.isSuccess && (
        <p><em>Resolved Deezer for {resolveAll.data.resolved} / {resolveAll.data.total} artists.</em></p>
      )}

      {isPending && <p><em>Loading…</em></p>}

      {isError && (
        <p className="error">Failed to load artists: {(error as Error).message}</p>
      )}

      {artists && artists.length === 0 && (
        <p><em>Catalog is empty — hit “Refresh from Plex” to populate it.</em></p>
      )}

      {artists && artists.length > 0 && (
        <>
          <div className="artist-search">
            <input
              type="text"
              value={query}
              placeholder={`Search ${artists.length} artists…`}
              onChange={(e) => onSearch(e.target.value)}
            />
            {query && <span className="artist-search-count">{filtered.length} match</span>}
          </div>

          <table className="table">
            <thead>
              <tr>
                {user && <th style={{ width: '5rem' }}></th>}
                <th>Name</th>
              </tr>
            </thead>
            <tbody>
              {paged.map((artist) => {
                const name = artist.artistKey.artistName
                return (
                  <ArtistRow
                    key={name}
                    artist={artist}
                    verdict={verdictByArtist.get(name)}
                    isOpen={expanded === name}
                    user={!!user}
                    ratePending={rateArtist.isPending}
                    onRate={(artistName, verdict, current) =>
                      rateArtist.mutate({ artist: artistName, verdict, current })
                    }
                    onToggle={toggleExpanded}
                    onCorrect={setCorrecting}
                  />
                )
              })}
            </tbody>
          </table>

          {pageCount > 1 && (
            <div className="disc-pager">
              <button disabled={safePage === 0} onClick={() => setPage(safePage - 1)}>
                ‹ prev
              </button>
              <span>page {safePage + 1} / {pageCount}</span>
              <button disabled={safePage >= pageCount - 1} onClick={() => setPage(safePage + 1)}>
                next ›
              </button>
            </div>
          )}
        </>
      )}

      {correcting && (
        <CorrectPicker artist={correcting} onClose={() => setCorrecting(null)} onApplied={afterCorrection} />
      )}
    </section>
  )
}
