import { useEffect, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getDeezerPlayInfo } from '../api/deezer'

// Only one preview should play at a time across the whole page; track the active element so
// starting a new one pauses the previous (e.g. expanding a second row in the list view).
let currentAudio: HTMLAudioElement | null = null

// Plays an artist's top-5 30-second Deezer previews in a plain <audio> (no iframe/login/cookies,
// works on mobile) and links out to the full Deezer artist page. Playback only ever starts from a
// user clicking a track — never automatically.
export function DeezerSample({ artist }: { artist: string }) {
  const { data, isPending, isError } = useQuery({
    queryKey: ['deezer-play', artist],
    queryFn: () => getDeezerPlayInfo(artist),
    staleTime: 60 * 60 * 1000,
  })
  const audioRef = useRef<HTMLAudioElement | null>(null)
  const [selected, setSelected] = useState<number | null>(null)
  const [playing, setPlaying] = useState(false)

  const play = (index: number) => {
    const el = audioRef.current
    const track = data?.tracks[index]
    if (!el || !track) return
    if (currentAudio && currentAudio !== el) currentAudio.pause()
    currentAudio = el
    el.src = track.previewUrl
    setSelected(index)
    el.play().catch(() => {
      /* autoplay can be blocked until a gesture — the play button still works */
    })
  }

  const toggle = (index: number) => {
    if (selected === index && playing) audioRef.current?.pause()
    else play(index)
  }

  // Stop our audio (and release the "current" slot) when this player unmounts.
  useEffect(() => {
    return () => {
      const el = audioRef.current
      if (el) el.pause()
      if (currentAudio === el) currentAudio = null
    }
  }, [])

  if (isPending) {
    return <span className="deezer-sample muted">Loading Deezer…</span>
  }
  if (isError || !data) {
    return <span className="deezer-sample muted">Not on Deezer</span>
  }

  return (
    <div className="deezer-sample">
      <div className="sample-head">
        <span className="sample-label">Top tracks</span>
        <a className="deezer-link" href={data.artistLink} target="_blank" rel="noopener noreferrer">
          Deezer ↗
        </a>
      </div>

      {data.tracks.length === 0 ? (
        <span className="sample-title muted">No previews available</span>
      ) : (
        <ul className="sample-list">
          {data.tracks.map((track, i) => (
            <li key={i}>
              <button
                className="sample-btn"
                onClick={() => toggle(i)}
                aria-label={selected === i && playing ? `Pause ${track.title}` : `Play ${track.title}`}
              >
                {selected === i && playing ? '⏸' : '▶'}
              </button>
              <span className={selected === i ? 'sample-title active' : 'sample-title'} title={track.title}>
                {track.title}
              </span>
            </li>
          ))}
        </ul>
      )}

      <audio
        ref={audioRef}
        preload="none"
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onEnded={() => setPlaying(false)}
      />
    </div>
  )
}
