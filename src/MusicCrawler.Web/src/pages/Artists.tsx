import { useQuery } from '@tanstack/react-query'
import { getArtists } from '../api/artists'

export default function Artists() {
  const { data: artists, isPending, isError, error } = useQuery({
    queryKey: ['artists'],
    queryFn: getArtists,
  })

  return (
    <section>
      <h1>Artists</h1>

      {isPending && <p><em>Loading…</em></p>}

      {isError && (
        <p className="error">Failed to load artists: {(error as Error).message}</p>
      )}

      {artists && (
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
