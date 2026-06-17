import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getArtists, refreshCatalog } from '../api/artists'
import { clearRating, getRatings, rate, type Verdict } from '../api/discovery'
import type { DiscoveryStatus, FeedItem } from '../types'
import { useAuth } from '../auth/AuthContext'

const verdictStatus = (v: Verdict): DiscoveryStatus => (v === 'up' ? 'Liked' : 'Disliked')

export default function Artists() {
  const queryClient = useQueryClient()
  const { user } = useAuth()

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
      }
      return current === verdictStatus(verdict) ? clearRating(item) : rate(item, verdict)
    },
    onSuccess: invalidate,
  })

  const refresh = useMutation({
    mutationFn: refreshCatalog,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['artists'] }),
  })

  return (
    <section>
      <div style={{ display: 'flex', alignItems: 'baseline', gap: '1rem' }}>
        <h1>Artists</h1>
        {/* The catalog auto-syncs on startup and daily; this manual trigger is a dev-only
            convenience and is compiled out of production builds. */}
        {import.meta.env.DEV && (
          <button onClick={() => refresh.mutate()} disabled={refresh.isPending}>
            {refresh.isPending ? 'Refreshing…' : 'Refresh from Plex (dev)'}
          </button>
        )}
      </div>

      {user && (
        <p>
          <em>Thumb the bands you own — 👍 feeds your recommendations, 👎 marks them off-taste.</em>
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

      {isPending && <p><em>Loading…</em></p>}

      {isError && (
        <p className="error">Failed to load artists: {(error as Error).message}</p>
      )}

      {artists && artists.length === 0 && (
        <p><em>Catalog is empty — hit “Refresh from Plex” to populate it.</em></p>
      )}

      {artists && artists.length > 0 && (
        <table className="table">
          <thead>
            <tr>
              {user && <th style={{ width: '5rem' }}></th>}
              <th>Name</th>
            </tr>
          </thead>
          <tbody>
            {artists.map((artist) => {
              const name = artist.artistKey.artistName
              const verdict = verdictByArtist.get(name)
              return (
                <tr key={name}>
                  {user && (
                    <td>
                      <div className="rate-cell">
                        <button
                          className={verdict === 'Liked' ? 'disc-btn up active' : 'disc-btn up'}
                          title={verdict === 'Liked' ? 'Clear rating' : 'Thumbs up'}
                          disabled={rateArtist.isPending}
                          onClick={() => rateArtist.mutate({ artist: name, verdict: 'up', current: verdict })}
                        >
                          👍
                        </button>
                        <button
                          className={verdict === 'Disliked' ? 'disc-btn down active' : 'disc-btn down'}
                          title={verdict === 'Disliked' ? 'Clear rating' : 'Thumbs down'}
                          disabled={rateArtist.isPending}
                          onClick={() => rateArtist.mutate({ artist: name, verdict: 'down', current: verdict })}
                        >
                          👎
                        </button>
                      </div>
                    </td>
                  )}
                  <td>{name}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </section>
  )
}
