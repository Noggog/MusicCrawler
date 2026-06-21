import { useEffect, useState, type CSSProperties } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getArtists } from '../api/artists'
import { clearSource, getArtistSources, pinSource, searchSource, unlinkSource } from '../api/sources'
import { getArtistLibraries } from '../api/library'
import { clearRating, getArtistDiscography, getRatings, rate, type Verdict } from '../api/discovery'
import { getRelated } from '../api/related'
import { useArtAccent } from '../art/artColors'
import { rateFeedback } from '../effects/effectsBus'
import type { ArtistAlbumItem, ArtistListItem, DiscoveryStatus, FeedItem, SourceCandidate, SourceIdentity } from '../types'
import { useAuth } from '../auth/AuthContext'
import { DeezerSample } from '../components/DeezerSample'
import { IconApprove, IconCheck, IconClear, IconReject, IconWrench } from '../components/icons'

// The detail pane is driven by a lightweight selection: just enough to render the readout and to key
// the Albums / Related tab queries. A library row supplies the full ArtistListItem (looked up by name
// for the Deezer link, genres, fans, correction); a related-artist card the user drills into may not
// be in the library, so all we can carry is its name + photo — the tabs still work off the name.
type SelectedArtist = { name: string; imageUrl: string | null }
type DetailTab = 'albums' | 'related' | 'meta' | 'library'

// Human labels for the source keys the backend emits.
const SOURCE_LABELS: Record<string, string> = {
  deezer: 'Deezer',
  musicbrainz: 'MusicBrainz',
  listenbrainz: 'ListenBrainz',
}
const sourceLabel = (s: string) => SOURCE_LABELS[s] ?? s

const verdictStatus = (v: Verdict): DiscoveryStatus => (v === 'up' ? 'Liked' : 'Disliked')

// The full library loads in one fetch, but rendering every row at once is the costly part — each
// row extracts an accent colour from its photo — so we page the rendered rows. Search still spans
// the whole library (it filters before paging).
const PAGE_SIZE = 25

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

// The "Meta" tab: every external metadata source's resolved identity for the selected library
// artist — its id, a link out to the source's page, the override flag — plus a per-source wrench
// (Correct) button for correctable sources (Deezer, MusicBrainz). ListenBrainz appears as a
// read-only link (its identity is just the MusicBrainz MBID).
function MetaTab({ artist }: { artist: string }) {
  const queryClient = useQueryClient()
  const [correcting, setCorrecting] = useState<SourceIdentity | null>(null)

  const { data, isPending, isError } = useQuery({
    queryKey: ['artist-sources', artist],
    queryFn: () => getArtistSources(artist),
  })

  // A pin/clear changes the resolved Deezer id, which in turn changes the discography (album list +
  // cover art) and re-derives similarity edges — refresh this tab, the discography, the artist list
  // (Deezer columns / suspect badge) and the downstream feeds. Without the discography invalidation a
  // relink left the old albums (and missing/stale covers) on screen until a hard refresh.
  const afterChange = () => {
    queryClient.invalidateQueries({ queryKey: ['artist-sources', artist] })
    queryClient.invalidateQueries({ queryKey: ['artist-discography', artist] })
    queryClient.invalidateQueries({ queryKey: ['artists'] })
    queryClient.invalidateQueries({ queryKey: ['feed'] })
    queryClient.invalidateQueries({ queryKey: ['related'] })
    setCorrecting(null)
  }

  if (isPending) return <div className="disc-sub-albums"><em className="disc-sub-note">Loading sources…</em></div>
  if (isError) return <div className="disc-sub-albums"><em className="disc-sub-note">Failed to load sources.</em></div>

  return (
    <div className="source-list">
      {data.sources.map((s) => (
        <div className="source-row" key={s.source}>
          <span className="source-badge">{sourceLabel(s.source)}</span>
          <div className="source-meta">
            {s.id ? (
              <>
                <span className="source-name">{s.name ?? '(unknown)'}</span>
                <span className="source-sub">
                  {s.detail ? `${s.detail} · ` : ''}
                  {s.id}
                  {s.isOverride ? ' · pinned' : ''}
                </span>
              </>
            ) : s.unlinked ? (
              <span className="source-sub"><em>Detached — won’t auto-resolve</em></span>
            ) : (
              <span className="source-sub"><em>Not resolved yet</em></span>
            )}
          </div>
          {s.link && (
            <a className="deezer-link" href={s.link} target="_blank" rel="noopener noreferrer">
              {sourceLabel(s.source)} ↗
            </a>
          )}
          {s.correctable && (
            <button
              className="disc-btn"
              title={`Correct ${sourceLabel(s.source)} association`}
              onClick={() => setCorrecting(s)}
            >
              <IconWrench size={16} />
            </button>
          )}
        </div>
      ))}

      {correcting && (
        <SourcePicker
          artist={artist}
          source={correcting}
          onClose={() => setCorrecting(null)}
          onApplied={afterChange}
        />
      )}
    </div>
  )
}

// Inline picker to pin the correct artist on one source. Searches that source (prefilled with the
// library name) and lets the user pick the right candidate. We surface each candidate's link rather
// than an audio preview, because verifying by name is exactly what's unreliable here.
function SourcePicker({
  artist,
  source,
  onClose,
  onApplied,
}: {
  artist: string
  source: SourceIdentity
  onClose: () => void
  onApplied: () => void
}) {
  const key = source.source
  const label = sourceLabel(key)
  const [query, setQuery] = useState(artist)

  const search = useQuery({
    queryKey: ['source-search', key, query],
    queryFn: () => searchSource(key, query),
    enabled: query.trim().length > 0,
  })

  const apply = useMutation({
    mutationFn: (id: string) => pinSource(key, artist, id),
    onSuccess: onApplied,
  })

  const reset = useMutation({
    mutationFn: () => clearSource(key, artist),
    onSuccess: onApplied,
  })

  const unlink = useMutation({
    mutationFn: () => unlinkSource(key, artist),
    onSuccess: onApplied,
  })

  const busy = apply.isPending || reset.isPending || unlink.isPending

  return (
    <div className="picker-backdrop" onClick={onClose}>
      <div className="picker-panel" onClick={(e) => e.stopPropagation()}>
        <div className="picker-head">
          <h2>Correct {label} for “{artist}”</h2>
          <button className="auth-btn" onClick={onClose}>Close</button>
        </div>
        <p>
          <em>Pick the right {label} artist — use the ↗ link to confirm before applying.</em>
        </p>

        <input
          className="picker-search"
          type="text"
          value={query}
          autoFocus
          placeholder={`Search ${label}…`}
          onChange={(e) => setQuery(e.target.value)}
        />

        {source.unlinked ? (
          <p className="picker-pinned">
            Detached — {label} has no match for this artist.{' '}
            <button className="link-btn" onClick={() => reset.mutate()} disabled={busy}>
              Re-enable automatic resolution
            </button>
          </p>
        ) : (
          <p className="picker-pinned">
            {source.isOverride
              ? `Pinned to ${label} ${source.id}. `
              : source.id
                ? `Auto-linked to ${label} ${source.id}. `
                : `Not linked to ${label} yet. `}
            {source.isOverride && (
              <>
                <button className="link-btn" onClick={() => reset.mutate()} disabled={busy}>
                  Reset to automatic
                </button>{' '}
              </>
            )}
            <button className="link-btn" onClick={() => unlink.mutate()} disabled={busy}>
              Unlink — no match on {label}
            </button>
          </p>
        )}

        {search.isPending && query.trim() && <p><em>Searching…</em></p>}
        {search.isError && <p className="error">Search failed.</p>}

        <ul className="picker-results">
          {(search.data ?? []).map((c: SourceCandidate) => {
            const current = c.id === source.id
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
                    {c.detail ? `${c.detail} · ` : ''}
                    {c.id}
                    {current && ' · current'}
                  </span>
                </div>
                {c.link && (
                  <a className="deezer-link" href={c.link} target="_blank" rel="noopener noreferrer">
                    {label} ↗
                  </a>
                )}
                <button
                  className="auth-btn"
                  disabled={busy || current}
                  onClick={() => apply.mutate(c.id)}
                >
                  {current ? 'In use' : 'Use this'}
                </button>
              </li>
            )
          })}
          {search.data && search.data.length === 0 && query.trim() && (
            <li><em>No {label} matches.</em></li>
          )}
        </ul>

        {apply.isError && <p className="error">Failed to apply: {(apply.error as Error).message}</p>}
        {unlink.isError && <p className="error">Failed to unlink: {(unlink.error as Error).message}</p>}
        {reset.isError && <p className="error">Failed to reset: {(reset.error as Error).message}</p>}
      </div>
    </div>
  )
}

// The "Library" tab: where the selected artist lives in the user's media servers (Plex now,
// Navidrome eventually), with a deep link to open the artist there. Reuses the source-row styling.
function LibraryTab({ artist }: { artist: string }) {
  const { data, isPending, isError } = useQuery({
    queryKey: ['artist-libraries', artist],
    queryFn: () => getArtistLibraries(artist),
  })

  if (isPending) return <div className="disc-sub-albums"><em className="disc-sub-note">Loading libraries…</em></div>
  if (isError) return <div className="disc-sub-albums"><em className="disc-sub-note">Failed to load libraries.</em></div>

  return (
    <div className="source-list">
      {data.sources.map((s) => (
        <div className="source-row" key={s.source}>
          <span className="source-badge">{s.label}</span>
          <div className="source-meta">
            {s.present ? (
              <span className="source-sub">In this library</span>
            ) : (
              <span className="source-sub"><em>Not in this library</em></span>
            )}
          </div>
          {s.links.map((l) => (
            <a className="deezer-link" key={l.url} href={l.url} target="_blank" rel="noopener noreferrer">
              {l.label} ↗
            </a>
          ))}
        </div>
      ))}
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
// shared `.disc-sub-album` styling turns that into the tinted card + the cover's glow). When the album
// has a Deezer id, the whole row toggles a 30-second track-preview player below it (like Discover); the
// action cluster stops the click so a thumb doesn't also open/close the preview.
function AlbumSubRow({
  a,
  busy,
  isOpen,
  onToggle,
  onRate,
  onClear,
}: {
  a: ArtistAlbumItem
  busy: boolean
  isOpen: boolean
  onToggle: () => void
  onRate: (a: ArtistAlbumItem, verdict: Verdict) => void
  onClear: (a: ArtistAlbumItem) => void
}) {
  const accent = useArtAccent(a.imageUrl)
  const accentStyle = accent ? ({ '--art-accent': accent } as CSSProperties) : undefined
  const label = a.verdict ? ALBUM_VERDICT_LABEL[a.verdict] : null
  const canPlay = a.deezerAlbumId != null
  return (
    <div className="disc-sub-album-wrap">
      <div
        className={`disc-sub-album${isOpen ? ' selected' : ''}${canPlay ? '' : ' no-play'}${a.owned ? ' owned' : ''}`}
        style={accentStyle}
        onClick={canPlay ? onToggle : undefined}
      >
        <AlbumThumb item={a} />
        <div className="disc-sub-album-name">{a.album}</div>
        <div className="disc-actions" onClick={(e) => e.stopPropagation()}>
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
      {isOpen && a.deezerAlbumId != null && <DeezerSample albumId={a.deezerAlbumId} />}
    </div>
  )
}

// The readout's Albums tab: the selected artist's full Deezer discography, owned albums flagged and
// missing ones thumbable so they can be queued to buy (or dismissed) right here — no trip through
// Discover. Fetched on demand (one Deezer call) only when the Albums tab is shown for an artist.
function ArtistAlbums({ artist }: { artist: string }) {
  const queryClient = useQueryClient()
  // Which album's Deezer preview is expanded — one at a time, like selecting a row in Discover.
  const [openAlbum, setOpenAlbum] = useState<string | null>(null)
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
          isOpen={openAlbum === a.album}
          onToggle={() => setOpenAlbum((cur) => (cur === a.album ? null : a.album))}
          onRate={(album, verdict) => rateAlbum.mutate({ a: album, verdict })}
          onClear={(album) => clearAlbum.mutate(album)}
        />
      ))}
    </div>
  )
}

// Artist photo (or a coloured initial), shared by the list rows and the detail hero. `hero` drops the
// inline size so CSS (.detail-hero) drives the large readout image.
function ArtistAvatar({ name, image, size, hero }: { name: string; image: string | null; size?: number; hero?: boolean }) {
  if (image) {
    return <img className="disc-avatar" src={image} alt={name} width={hero ? undefined : size} height={hero ? undefined : size} loading="lazy" />
  }
  return (
    <div className="disc-avatar disc-avatar-fallback" style={hero ? undefined : { width: size, height: size, fontSize: (size ?? 40) / 2.5 }}>
      {name.charAt(0).toUpperCase()}
    </div>
  )
}

// One artist in the left-hand list, themed from its photo via `--art-accent` (matching the Discover
// feed). Clicking the row opens it in the readout; the rate cluster (signed-in only) stops the click
// so a thumb doesn't also re-select the row.
function ArtistListRow({
  artist,
  verdict,
  selected,
  user,
  ratePending,
  onSelect,
  onRate,
}: {
  artist: ArtistListItem
  verdict: DiscoveryStatus | undefined
  selected: boolean
  user: boolean
  ratePending: boolean
  onSelect: (artist: ArtistListItem) => void
  onRate: (name: string, verdict: Verdict, current?: DiscoveryStatus) => void
}) {
  const name = artist.artistKey.artistName
  const suspect = isSuspect(artist)
  const accent = useArtAccent(artist.artistImageUrl)
  const accentStyle = accent ? ({ '--art-accent': accent } as CSSProperties) : undefined
  return (
    <div className={selected ? 'disc-row selected' : 'disc-row'} style={accentStyle} onClick={() => onSelect(artist)}>
      <ArtistAvatar name={name} image={artist.artistImageUrl} size={52} />
      <div className="disc-row-main">
        <div className="disc-name">
          {name}
          {suspect && (
            <span className="warn-badge" title="Deezer name doesn't match — likely the wrong artist"> ⚠</span>
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
        <div className="disc-actions" onClick={(e) => e.stopPropagation()}>
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
      )}
    </div>
  )
}

// The "Related" tab: the artists that stem from the selected one, unified across similarity sources
// (the same /related graph the Discover feed is built from). Each card drills the readout into that
// artist so you can walk the graph; a library artist lands on its full readout, a stranger on a
// lighter one. Fetched on demand only when the tab is open.
function RelatedTab({ artist, onExplore }: { artist: string; onExplore: (sel: SelectedArtist) => void }) {
  const { data, isPending, isError } = useQuery({
    queryKey: ['related', artist],
    queryFn: () => getRelated(artist),
    staleTime: 5 * 60 * 1000,
  })

  if (isPending) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">Finding related artists…</em></div>
  }
  if (isError || !data) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">Couldn’t load related artists.</em></div>
  }
  if (data.related.length === 0) {
    return <div className="disc-sub-albums"><em className="disc-sub-note">No related artists found on Deezer.</em></div>
  }

  return (
    <div className="related-grid artist-related-grid">
      {data.related.map((r) => {
        const rname = r.artistKey.artistName
        return (
          <div
            className="related-card"
            key={rname}
            onClick={() => onExplore({ name: rname, imageUrl: r.imageUrl })}
            title={`Explore ${rname}`}
          >
            {r.imageUrl ? (
              <img src={r.imageUrl} alt={rname} loading="lazy" />
            ) : (
              <div className="related-card-noimg">no image</div>
            )}
            <div className="related-card-name">{rname}</div>
            {r.sources.length > 0 && (
              <div className="related-card-sources">
                {r.sources.map((s) => (
                  <span className="source-badge" key={s}>{s}</span>
                ))}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

// The right-hand readout for the artist selected in the list (desktop) / a bottom drawer (mobile): a
// big hero, the Deezer link-out / fans / genres, the rate + correct actions, and a tab strip whose
// panels are the artist's albums (discography drill-down) and the artists related to them.
function DetailPane({
  selected,
  libItem,
  verdict,
  user,
  tab,
  ratePending,
  onTab,
  onRate,
  onExplore,
  onClose,
}: {
  selected: SelectedArtist | null
  libItem: ArtistListItem | undefined
  verdict: DiscoveryStatus | undefined
  user: boolean
  tab: DetailTab
  ratePending: boolean
  onTab: (tab: DetailTab) => void
  onRate: (name: string, verdict: Verdict, current?: DiscoveryStatus) => void
  onExplore: (sel: SelectedArtist) => void
  onClose: () => void
}) {
  // Resolve art + accent unconditionally (hooks run before the empty-state early return). Prefer the
  // library photo when the selection is an owned artist, falling back to whatever the card carried.
  const image = libItem?.artistImageUrl ?? selected?.imageUrl ?? null
  const accent = useArtAccent(image)
  if (!selected) {
    return (
      <aside className="disc-detail is-empty">
        <div className="disc-detail-empty">
          <span className="detail-empty-icon">🎧</span>
        </div>
      </aside>
    )
  }

  const accentStyle = accent ? ({ '--art-accent': accent } as CSSProperties) : undefined
  const name = selected.name
  const suspect = !!libItem && isSuspect(libItem)
  const deezerHref =
    libItem?.deezerLink ?? (libItem?.deezerId != null ? `https://www.deezer.com/artist/${libItem.deezerId}` : null)

  return (
    <aside className="disc-detail" style={accentStyle}>
      <button className="detail-close" title="Close" onClick={onClose}>✕</button>

      <div className="detail-header">
        <div className="detail-hero">
          <ArtistAvatar name={name} image={image} hero />
        </div>

        <div className="detail-headinfo">
          {!libItem && <span className="detail-chip">Not in your library</span>}
          <h2 className="detail-name">
            {deezerHref ? (
              <a
                className="artist-name-link"
                href={deezerHref}
                target="_blank"
                rel="noopener noreferrer"
                title={suspect && libItem?.deezerName ? `Deezer: ${libItem.deezerName} — likely the wrong artist` : libItem?.deezerName ?? undefined}
              >
                {name}
              </a>
            ) : (
              name
            )}
            {suspect && <span className="warn-badge" title="Deezer name doesn't match — likely the wrong artist"> ⚠</span>}
          </h2>

          {libItem?.deezerFans != null && (
            <div className="detail-meta">{formatFans(libItem.deezerFans)} fans on Deezer</div>
          )}

          {libItem && libItem.genres.length > 0 && (
            <div className="detail-chips">
              {libItem.genres.slice(0, 6).map((g) => (
                <span className="detail-chip" key={g}>{g}</span>
              ))}
            </div>
          )}

          {/* Thumbs work for any selected artist: an owned one records a library rating, a related
              stranger drilled into from the Related tab gets liked straight into the buy list.
              Source corrections live in the Sources tab (library artists only). */}
          {user && (
            <div className="detail-actions">
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
          )}
        </div>
      </div>

      {/* Sample the artist's top tracks (30s Deezer previews) right in the readout, like Discover.
          The whole pane is keyed by selected artist (see the DetailPane render), so it remounts on
          selection change — no inner key needed (and an inner key={name} here collides with the
          albums/related panel's, which is the same name, since they're siblings under <aside>). */}
      <DeezerSample artist={name} />

      <div className="artist-detail-tabs" role="tablist">
        <button
          role="tab"
          aria-selected={tab === 'albums'}
          className={tab === 'albums' ? 'artist-tab active' : 'artist-tab'}
          onClick={() => onTab('albums')}
        >
          Albums
        </button>
        <button
          role="tab"
          aria-selected={tab === 'related'}
          className={tab === 'related' ? 'artist-tab active' : 'artist-tab'}
          onClick={() => onTab('related')}
        >
          Related artists
        </button>
        {/* Meta (source identities) and Library (Plex/Navidrome presence) only apply to library
            artists — a related-artist stranger drilled in from the Related tab has no catalog row. */}
        {libItem && (
          <button
            role="tab"
            aria-selected={tab === 'meta'}
            className={tab === 'meta' ? 'artist-tab active' : 'artist-tab'}
            onClick={() => onTab('meta')}
          >
            Meta
          </button>
        )}
        {libItem && (
          <button
            role="tab"
            aria-selected={tab === 'library'}
            className={tab === 'library' ? 'artist-tab active' : 'artist-tab'}
            onClick={() => onTab('library')}
          >
            Library
          </button>
        )}
      </div>

      {tab === 'meta' ? (
        libItem ? (
          user ? (
            <MetaTab artist={name} />
          ) : (
            <div className="disc-sub-albums"><em className="disc-sub-note">Log in to view this artist’s metadata sources.</em></div>
          )
        ) : (
          <div className="disc-sub-albums"><em className="disc-sub-note">Metadata sources apply to library artists.</em></div>
        )
      ) : tab === 'library' ? (
        libItem ? (
          user ? (
            <LibraryTab artist={name} />
          ) : (
            <div className="disc-sub-albums"><em className="disc-sub-note">Log in to view this artist’s libraries.</em></div>
          )
        ) : (
          <div className="disc-sub-albums"><em className="disc-sub-note">Library links apply to library artists.</em></div>
        )
      ) : tab === 'albums' ? (
        user ? (
          // The whole pane is keyed by selected artist, so the discography refetches/remounts cleanly
          // on selection change — no inner key needed.
          <ArtistAlbums artist={name} />
        ) : (
          <div className="disc-sub-albums"><em className="disc-sub-note">Log in to view this artist’s albums.</em></div>
        )
      ) : (
        <RelatedTab artist={name} onExplore={onExplore} />
      )}
    </aside>
  )
}

export default function Artists() {
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(0)
  // The artist open in the right-hand readout (desktop) / drawer (mobile), and which of its tabs is
  // showing. Carried as a lightweight selection so a related-artist card the user drills into — which
  // may not be in the library — can still drive the readout.
  const [selected, setSelected] = useState<SelectedArtist | null>(null)
  const [tab, setTab] = useState<DetailTab>('albums')

  // Editing the search resets to the first page so matches are never hidden on a later page.
  const onSearch = (next: string) => {
    setQuery(next)
    setPage(0)
  }

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

  // Thumbing an artist — works for any selected artist, owned or not (a related artist drilled into
  // from the Related tab can be liked straight into the buy list, an alternative to the Discover
  // pipeline). The kind is cosmetic to rate()/clearRating() (they send only the name), but we set it
  // honestly from library membership. Clicking the verdict that's already set clears it back to neutral.
  const rateArtist = useMutation({
    mutationFn: ({ artist, verdict, current }: { artist: string; verdict: Verdict; current?: DiscoveryStatus }) => {
      const inLibrary = (artists ?? []).some((a) => a.artistKey.artistName === artist)
      const item: FeedItem = {
        kind: inLibrary ? 'LibraryArtist' : 'RecommendedArtist',
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

  const filtered = (artists ?? []).filter((a) =>
    normalize(a.artistKey.artistName).includes(normalize(query)),
  )

  // Clamp to a valid page after the filter shrinks (e.g. a search that lands past the current page).
  const pageCount = Math.max(1, Math.ceil(filtered.length / PAGE_SIZE))
  const safePage = Math.min(page, pageCount - 1)
  const paged = filtered.slice(safePage * PAGE_SIZE, safePage * PAGE_SIZE + PAGE_SIZE)

  // The full library item behind the current selection, when it's an owned artist (undefined for a
  // related-artist stranger drilled into from the Related tab). Drives the readout's Deezer link,
  // genres, fans and the rate/correct actions.
  const libItem = selected ? (artists ?? []).find((a) => a.artistKey.artistName === selected.name) : undefined

  // On first visit, drop the user onto a random page and open a random artist from it, so the page
  // is a fresh jumping-off point each time rather than always the alphabetical top. Runs once (gated
  // by `randomized` so it never re-randomizes when the user closes the readout or pages around). Using
  // state, not a ref, is deliberate: it defers the reopen-on-empty effect below to the *next* commit,
  // so it can't clobber the random selection in the same render. The random selection is desktop-only
  // — on mobile the readout is a drawer, so we leave it closed (just the random page) until a row is tapped.
  const [randomized, setRandomized] = useState(false)
  useEffect(() => {
    if (randomized) return
    const list = artists ?? []
    if (list.length === 0) return
    setRandomized(true)
    const totalPages = Math.max(1, Math.ceil(list.length / PAGE_SIZE))
    const randPage = Math.floor(Math.random() * totalPages)
    setPage(randPage)
    if (typeof window !== 'undefined' && window.matchMedia('(min-width: 961px)').matches) {
      const pageItems = list.slice(randPage * PAGE_SIZE, randPage * PAGE_SIZE + PAGE_SIZE)
      const pick = pageItems[Math.floor(Math.random() * pageItems.length)]
      if (pick) setSelected({ name: pick.artistKey.artistName, imageUrl: pick.artistImageUrl })
    }
  }, [artists, randomized])

  // Once randomized, keep the readout populated: if it ends up empty (e.g. the user closes it), reopen
  // the current page's first artist so the pane never falls back to the bare placeholder. Desktop only.
  const firstItem = paged[0]
  useEffect(() => {
    if (!firstItem || selected || !randomized) return
    if (typeof window !== 'undefined' && !window.matchMedia('(min-width: 961px)').matches) return
    setSelected({ name: firstItem.artistKey.artistName, imageUrl: firstItem.artistImageUrl })
  }, [firstItem, selected, randomized])

  // On mobile the readout takes over the screen; lock the background list so it can't scroll
  // (or peek through the translucent top bar) behind it. CSS scopes the lock to the mobile breakpoint.
  useEffect(() => {
    document.body.classList.toggle('detail-open', selected != null)
    return () => document.body.classList.remove('detail-open')
  }, [selected])

  return (
    <section>
      <div className="artists-header">
        <h1>Artists</h1>
        {artists && artists.length > 0 && (
          <div className="artist-search">
            <input
              type="text"
              value={query}
              placeholder={`Search ${artists.length} artists…`}
              onChange={(e) => onSearch(e.target.value)}
            />
            {query && <span className="artist-search-count">{filtered.length} match</span>}
          </div>
        )}
      </div>

      {isPending && <p><em>Loading…</em></p>}

      {isError && (
        <p className="error">Failed to load artists: {(error as Error).message}</p>
      )}

      {artists && artists.length === 0 && (
        <p><em>Catalog is empty — hit “Refresh from Plex” to populate it.</em></p>
      )}

      {artists && artists.length > 0 && (
        <>
          <div className="disc-layout">
            <div className="disc-main">
              <div className="disc-list">
                {paged.map((artist) => {
                  const name = artist.artistKey.artistName
                  return (
                    <ArtistListRow
                      key={name}
                      artist={artist}
                      verdict={verdictByArtist.get(name)}
                      selected={selected?.name === name}
                      user={!!user}
                      ratePending={rateArtist.isPending}
                      onSelect={(a) => setSelected({ name: a.artistKey.artistName, imageUrl: a.artistImageUrl })}
                      onRate={(artistName, verdict, current) =>
                        rateArtist.mutate({ artist: artistName, verdict, current })
                      }
                    />
                  )
                })}

                {filtered.length === 0 && (
                  <p className="disc-sub-note"><em>No artists match “{query}”.</em></p>
                )}
              </div>

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
            </div>

            <DetailPane
              // Key the whole pane by the selected artist so switching selection remounts it as one
              // atomic unit. Relying on inner key={name} props (the player, albums, related tabs) left
              // a window where a previous artist's Deezer player could linger as a stale sibling —
              // showing two "Top tracks" lists. One key on the pane closes that.
              key={selected?.name ?? '∅'}
              selected={selected}
              libItem={libItem}
              verdict={selected ? verdictByArtist.get(selected.name) : undefined}
              user={!!user}
              tab={tab}
              ratePending={rateArtist.isPending}
              onTab={setTab}
              onRate={(artistName, verdict, current) =>
                rateArtist.mutate({ artist: artistName, verdict, current })
              }
              onExplore={setSelected}
              onClose={() => setSelected(null)}
            />
          </div>
        </>
      )}
    </section>
  )
}
