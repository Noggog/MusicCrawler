import { useState, type FormEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthContext'
import { getRelated } from '../api/related'
import {
  clearPlexTags,
  reapplyPlexTags,
  rebuildPlexTags,
  type RebuildResult,
} from '../api/dev'

// The in-app dev panel: tooling that's only shown to (and only usable by) DEV_USERNAMES users.
// Absorbs the old Related (dev) similarity debugger and adds the Plex tag maintenance controls.
export default function Dev() {
  const { user, isLoading } = useAuth()

  if (isLoading) {
    return (
      <section>
        <h1>Dev tools</h1>
        <p><em>…</em></p>
      </section>
    )
  }

  // The route is rendered for everyone, but the panel is dev-only. The server enforces the same gate
  // on every endpoint, so this is just to avoid showing controls that would 403.
  if (!user?.isDev) {
    return (
      <section>
        <h1>Dev tools</h1>
        <p><em>Not available for this account.</em></p>
      </section>
    )
  }

  return (
    <section>
      <h1>Dev tools</h1>
      <PlexTagTools />
      <SimilarityDebug />
    </section>
  )
}

// ---- Plex tag maintenance ----

function PlexTagTools() {
  const [status, setStatus] = useState<string | null>(null)

  const clear = useMutation({
    mutationFn: clearPlexTags,
    onSuccess: (r) => setStatus(`Cleared managed tags from ${r.cleared} artist(s).`),
    onError: (e) => setStatus((e as Error).message),
  })
  const reapply = useMutation({
    mutationFn: reapplyPlexTags,
    onSuccess: (r) => setStatus(`Reapplied ${r.applied} tag(s) from stored ratings.`),
    onError: (e) => setStatus((e as Error).message),
  })
  const rebuild = useMutation({
    mutationFn: rebuildPlexTags,
    onSuccess: (r: RebuildResult) =>
      setStatus(`Rebuilt: cleared ${r.cleared} artist(s), reapplied ${r.applied} tag(s).`),
    onError: (e) => setStatus((e as Error).message),
  })

  const busy = clear.isPending || reapply.isPending || rebuild.isPending

  return (
    <div className="dev-tool">
      <h2>Plex tags</h2>
      <p>
        Per-user <code>&lt;username&gt;_liked</code> / <code>_disliked</code> labels mirrored onto
        artists in Plex. Clear nukes every managed tag; reapply re-derives them from stored ratings;
        rebuild does both (the true reset).
      </p>

      <div className="controls">
        <button
          onClick={() => {
            if (window.confirm('Remove every "_liked"/"_disliked" tag from all Plex artists?')) {
              setStatus(null)
              clear.mutate()
            }
          }}
          disabled={busy}
        >
          {clear.isPending ? 'Clearing…' : 'Clear'}
        </button>
        <button
          onClick={() => {
            setStatus(null)
            reapply.mutate()
          }}
          disabled={busy}
        >
          {reapply.isPending ? 'Reapplying…' : 'Reapply from ratings'}
        </button>
        <button
          onClick={() => {
            if (window.confirm('Wipe all managed tags, then reapply from current ratings?')) {
              setStatus(null)
              rebuild.mutate()
            }
          }}
          disabled={busy}
        >
          {rebuild.isPending ? 'Rebuilding…' : 'Rebuild'}
        </button>
      </div>

      {status && <p className="dev-status">{status}</p>}
    </div>
  )
}

// ---- Similarity graph debugger (formerly the Related (dev) page) ----

// A submission of the form: the artist to query, whether to force a re-fetch, and a nonce so
// every "Fetch" (even for the same artist) re-runs the query — handy when debugging staleness.
interface Query {
  name: string
  refresh: boolean
  nonce: number
}

function SimilarityDebug() {
  const [input, setInput] = useState('Radiohead')
  const [refresh, setRefresh] = useState(false)
  const [query, setQuery] = useState<Query | null>(null)

  const { data, isFetching, isError, error } = useQuery({
    queryKey: ['related', query?.name, query?.refresh, query?.nonce],
    queryFn: () => getRelated(query!.name, query!.refresh),
    enabled: query !== null,
  })

  function run(name: string, force: boolean) {
    const trimmed = name.trim()
    if (!trimmed) return
    setInput(trimmed)
    setQuery({ name: trimmed, refresh: force, nonce: Date.now() })
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    run(input, refresh)
  }

  return (
    <div className="dev-tool">
      <h2>Similarity graph</h2>
      <p>
        Hits <code>GET /related/{'{artist}'}</code> — ingests from Deezer on a cache miss / stale
        entry, persists the graph, then unifies across sources. Click a card to explore from it.
      </p>

      <form onSubmit={onSubmit} className="controls">
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="Artist name"
          aria-label="Artist name"
        />
        <label style={{ display: 'flex', alignItems: 'center', gap: '0.35rem' }}>
          <input
            type="checkbox"
            checked={refresh}
            onChange={(e) => setRefresh(e.target.checked)}
          />
          Force refresh
        </label>
        <button type="submit" disabled={isFetching}>
          {isFetching ? 'Fetching…' : 'Fetch related'}
        </button>
      </form>

      {isError && <p className="error">{(error as Error).message}</p>}

      {data && (
        <>
          <p>
            <em>
              {data.related.length} related artist{data.related.length === 1 ? '' : 's'} for{' '}
              <strong>{data.artist.artistName}</strong>
            </em>
          </p>

          {data.related.length === 0 ? (
            <p>
              <em>No related artists found (Deezer had no match, or returned none).</em>
            </p>
          ) : (
            <div className="related-grid">
              {data.related.map((r) => (
                <div
                  className="related-card"
                  key={r.artistKey.artistName}
                  onClick={() => run(r.artistKey.artistName, false)}
                  title={`Explore ${r.artistKey.artistName}`}
                >
                  {r.imageUrl ? (
                    <img src={r.imageUrl} alt={r.artistKey.artistName} loading="lazy" />
                  ) : (
                    <div className="related-card-noimg">no image</div>
                  )}
                  <div className="related-card-name">{r.artistKey.artistName}</div>
                  <div className="related-card-sources">
                    {r.sources.map((s) => (
                      <span className="source-badge" key={s}>
                        {s}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  )
}
