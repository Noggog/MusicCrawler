import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getArtists, refreshCatalog } from '../api/artists'

export default function Artists() {
  const queryClient = useQueryClient()

  const { data: artists, isPending, isError, error } = useQuery({
    queryKey: ['artists'],
    queryFn: getArtists,
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
              <th>Name</th>
            </tr>
          </thead>
          <tbody>
            {artists.map((artist) => (
              <tr key={artist.artistKey.artistName}>
                <td>{artist.artistKey.artistName}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
