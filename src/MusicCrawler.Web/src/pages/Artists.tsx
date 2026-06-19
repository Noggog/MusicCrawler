import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { clearDeezerId, getArtists, refreshCatalog, resolveAllDeezer, setDeezerId } from '../api/artists'
import { searchDeezerArtists } from '../api/deezer'
import { clearRating, getRatings, rate, type Verdict } from '../api/discovery'
import type { ArtistListItem, DeezerCandidate, DiscoveryStatus, FeedItem } from '../types'
import { useAuth } from '../auth/AuthContext'
import { IconApprove, IconReject, IconWrench } from '../components/icons'

const verdictStatus = (v: Verdict): DiscoveryStatus => (v === 'up' ? 'Liked' : 'Disliked')

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

export default function Artists() {
  const queryClient = useQueryClient()
  const { user } = useAuth()
  const [query, setQuery] = useState('')
  const [correcting, setCorrecting] = useState<ArtistListItem | null>(null)

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

  // artist name -> current verdict (artist ratings only; albums live on the Ratings/Discover pages).
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
              onChange={(e) => setQuery(e.target.value)}
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
              {filtered.map((artist) => {
                const name = artist.artistKey.artistName
                const verdict = verdictByArtist.get(name)
                const suspect = isSuspect(artist)
                return (
                  <tr className="artist-row" key={name}>
                    {user && (
                      <td>
                        <div className="rate-cell">
                          <button
                            className={verdict === 'Liked' ? 'disc-btn up active' : 'disc-btn up'}
                            title={verdict === 'Liked' ? 'Clear rating' : 'Approve'}
                            disabled={rateArtist.isPending}
                            onClick={() => rateArtist.mutate({ artist: name, verdict: 'up', current: verdict })}
                          >
                            <IconApprove />
                          </button>
                          <button
                            className={verdict === 'Disliked' ? 'disc-btn down active' : 'disc-btn down'}
                            title={verdict === 'Disliked' ? 'Clear rating' : 'Reject'}
                            disabled={rateArtist.isPending}
                            onClick={() => rateArtist.mutate({ artist: name, verdict: 'down', current: verdict })}
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
                            onClick={() => setCorrecting(artist)}
                          >
                            <IconWrench size={22} />
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </>
      )}

      {correcting && (
        <CorrectPicker artist={correcting} onClose={() => setCorrecting(null)} onApplied={afterCorrection} />
      )}
    </section>
  )
}
