// Dev-panel endpoints for the per-user Plex like/dislike labels. All are gated server-side by the
// "DevUser" policy (DEV_USERNAMES), so a non-dev hitting them gets a 403 regardless of the UI.

export interface ClearResult {
  cleared: number
}

export interface ReapplyResult {
  applied: number
}

export interface RebuildResult {
  cleared: number
  applied: number
}

// Strip every managed ("_liked"/"_disliked") label from every artist — clean slate.
export async function clearPlexTags(): Promise<ClearResult> {
  const res = await fetch('/api/dev/plex-tags/clear', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to clear Plex tags: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ClearResult
}

// Reapply tags from every user's stored ratings (additive — doesn't remove stale ones).
export async function reapplyPlexTags(): Promise<ReapplyResult> {
  const res = await fetch('/api/dev/plex-tags/reapply', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to reapply Plex tags: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as ReapplyResult
}

// Nuke then reapply — the full reset that brings Plex in line with current ratings.
export async function rebuildPlexTags(): Promise<RebuildResult> {
  const res = await fetch('/api/dev/plex-tags/rebuild', { method: 'POST' })
  if (!res.ok) {
    throw new Error(`Failed to rebuild Plex tags: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as RebuildResult
}
