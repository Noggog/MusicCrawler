// Per-user seed artists. All calls require an authenticated session (cookie sent automatically,
// same-origin). artist goes in the query string so names with '/' (e.g. "AC/DC") work.

export async function getSeeds(): Promise<string[]> {
  const res = await fetch('/api/seeds')
  if (!res.ok) {
    throw new Error(`Failed to load seeds: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as string[]
}

export async function addSeed(artist: string): Promise<void> {
  const res = await fetch(`/api/seeds?${new URLSearchParams({ artist })}`, { method: 'PUT' })
  if (!res.ok) {
    throw new Error(`Failed to add seed: ${res.status} ${res.statusText}`)
  }
}

export async function removeSeed(artist: string): Promise<void> {
  const res = await fetch(`/api/seeds?${new URLSearchParams({ artist })}`, { method: 'DELETE' })
  if (!res.ok) {
    throw new Error(`Failed to remove seed: ${res.status} ${res.statusText}`)
  }
}
