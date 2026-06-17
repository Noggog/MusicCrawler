import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  getCombinedArtists,
  resolveCombinedArtists,
  type CleanupResult,
  type CombinedNameEntry,
} from '../api/maintenance'
import { useAuth } from '../auth/AuthContext'

const SCOPE_LABEL: Record<CombinedNameEntry['scope'], string> = {
  catalog: 'Library artist',
  artistRating: 'Artist rating',
  albumRating: 'Album rating',
}

function ResultSummary({ result }: { result: CleanupResult }) {
  const parts = [
    result.catalogSplit > 0 && `${result.catalogSplit} library artist(s) split`,
    result.artistRatingsSplit > 0 && `${result.artistRatingsSplit} artist rating(s) re-attributed`,
    result.albumRatingsSplit > 0 && `${result.albumRatingsSplit} album rating(s) re-attributed`,
    result.pendingRemoved > 0 && `${result.pendingRemoved} stale recommendation(s) dropped`,
  ].filter(Boolean) as string[]

  return (
    <p className="cleanup-done">
      ✓ Done. {parts.length > 0 ? parts.join(', ') + '.' : 'Nothing needed changing.'}
    </p>
  )
}

export default function Cleanup() {
  const { user } = useAuth()
  const queryClient = useQueryClient()

  const { data, isPending, isError, error } = useQuery({
    queryKey: ['maintenance', 'combined-artists'],
    queryFn: getCombinedArtists,
    enabled: !!user,
  })

  const resolve = useMutation({
    mutationFn: resolveCombinedArtists,
    onSuccess: () => {
      // The sweep touches the catalog feed, ratings and the to-buy list — refresh them all.
      queryClient.invalidateQueries({ queryKey: ['maintenance'] })
      queryClient.invalidateQueries({ queryKey: ['feed'] })
      queryClient.invalidateQueries({ queryKey: ['ratings'] })
      queryClient.invalidateQueries({ queryKey: ['purchases'] })
    },
  })

  if (!user) {
    return (
      <section>
        <h1>Cleanup</h1>
        <p><em>Log in to run library maintenance.</em></p>
      </section>
    )
  }

  const entries = data ?? []

  return (
    <section>
      <h1>Cleanup {entries.length > 0 ? `(${entries.length})` : ''}</h1>
      <p className="disc-sub">
        <em>
          Plex sometimes joins collaborators into one name with a semicolon (e.g.{' '}
          <code>Nina Simone;Hot Chip</code>). These are really two artists. Resolving splits them
          apart in the library and re-attributes any ratings to each real artist.
        </em>
      </p>

      {isError && <p className="error">Failed to scan: {(error as Error).message}</p>}
      {resolve.isError && <p className="error">Cleanup failed: {(resolve.error as Error).message}</p>}
      {isPending && <p><em>Scanning…</em></p>}

      {resolve.isSuccess && !resolve.isPending && <ResultSummary result={resolve.data} />}

      {data && entries.length === 0 && !resolve.isSuccess && (
        <p><em>Nothing to clean up — no combined names found. 🎉</em></p>
      )}

      {entries.length > 0 && (
        <>
          <button
            className="disc-rebuild"
            onClick={() => resolve.mutate()}
            disabled={resolve.isPending}
          >
            {resolve.isPending ? 'Cleaning…' : `Clean up all ${entries.length}`}
          </button>

          <div className="disc-list cleanup-list">
            {entries.map((e) => (
              <div className="disc-row" key={`${e.scope}:${e.name}:${e.album ?? ''}`}>
                <div className="disc-row-main">
                  <span className="feed-badge">{SCOPE_LABEL[e.scope]}</span>
                  <div className="disc-name">
                    {e.name}
                    {e.album ? ` — ${e.album}` : ''}
                  </div>
                  <span className="disc-provenance">
                    → {e.splitInto.join(' + ')}
                    {e.affected > 1 ? ` (${e.affected} entries)` : ''}
                  </span>
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </section>
  )
}
