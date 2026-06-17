import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getArtists, refreshCatalog } from '../api/artists'
import { getSeeds, addSeed, removeSeed } from '../api/seeds'
import { useAuth } from '../auth/AuthContext'

export default function Artists() {
  const queryClient = useQueryClient()
  const { user } = useAuth()

  const { data: artists, isPending, isError, error } = useQuery({
    queryKey: ['artists'],
    queryFn: getArtists,
  })

  // Seeds are per-user, so only fetch them when signed in.
  const { data: seeds } = useQuery({
    queryKey: ['seeds'],
    queryFn: getSeeds,
    enabled: !!user,
  })
  const seedSet = new Set(seeds ?? [])

  const toggleSeed = useMutation({
    mutationFn: ({ artist, seeded }: { artist: string; seeded: boolean }) =>
      seeded ? removeSeed(artist) : addSeed(artist),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['seeds'] }),
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
          <em>Click the star to mark an artist as a seed for recommendations. {seedSet.size} seeded.</em>
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
              {user && <th style={{ width: '2.5rem' }}></th>}
              <th>Name</th>
            </tr>
          </thead>
          <tbody>
            {artists.map((artist) => {
              const name = artist.artistKey.artistName
              const seeded = seedSet.has(name)
              return (
                <tr key={name}>
                  {user && (
                    <td>
                      <button
                        className={seeded ? 'seed-btn seeded' : 'seed-btn'}
                        title={seeded ? 'Remove seed' : 'Add as seed'}
                        disabled={toggleSeed.isPending}
                        onClick={() => toggleSeed.mutate({ artist: name, seeded })}
                      >
                        {seeded ? '★' : '☆'}
                      </button>
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
