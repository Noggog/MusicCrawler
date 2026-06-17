// Deezer play info for an artist: up to 5 30-second previews to sample plus a link to the artist's
// Deezer page. Returns null when Deezer has no match (the backend answers 404). artist goes in the
// query string so names with '/' work.

export interface DeezerPreviewTrack {
  title: string
  previewUrl: string
}

export interface DeezerPlayInfo {
  id: number
  artistLink: string
  imageUrl: string | null
  tracks: DeezerPreviewTrack[]
}

export async function getDeezerPlayInfo(artist: string): Promise<DeezerPlayInfo | null> {
  const res = await fetch(`/api/deezer/artist?${new URLSearchParams({ artist })}`)
  if (res.status === 404) {
    return null
  }
  if (!res.ok) {
    throw new Error(`Failed to resolve Deezer artist: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DeezerPlayInfo
}

// Play info for a specific album id: its previewable tracks plus a link to the album's Deezer page.
export interface DeezerAlbumPlayInfo {
  id: number
  albumLink: string
  tracks: DeezerPreviewTrack[]
}

export async function getDeezerAlbumPlayInfo(albumId: number): Promise<DeezerAlbumPlayInfo> {
  const res = await fetch(`/api/deezer/album?${new URLSearchParams({ id: String(albumId) })}`)
  if (!res.ok) {
    throw new Error(`Failed to resolve Deezer album: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as DeezerAlbumPlayInfo
}
