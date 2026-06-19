import { setVolume, useVolume } from '../audio/volume'

// Sleek inline speaker glyph; the sound waves fade in as the level rises, and an "x" shows at mute.
function VolumeIcon({ volume }: { volume: number }) {
  const muted = volume === 0
  return (
    <svg
      className="volume-icon"
      width="18"
      height="18"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {/* speaker body */}
      <path d="M4 9v6h4l5 4V5L8 9H4z" fill="currentColor" stroke="none" />
      {muted ? (
        <path d="M17 9l5 6M22 9l-5 6" />
      ) : (
        <>
          <path d="M16 9a4 4 0 0 1 0 6" opacity={volume >= 0.33 ? 1 : 0.25} />
          <path d="M18.5 6.5a8 8 0 0 1 0 11" opacity={volume >= 0.66 ? 1 : 0.25} />
        </>
      )}
    </svg>
  )
}

// App-wide volume slider for Deezer previews; lives in the topbar.
export default function VolumeControl() {
  const volume = useVolume()
  const pct = Math.round(volume * 100)

  return (
    <div className="volume-box" title={`Preview volume: ${pct}%`}>
      <VolumeIcon volume={volume} />
      <input
        className="volume-slider"
        type="range"
        min={0}
        max={1}
        step={0.01}
        value={volume}
        onChange={(e) => setVolume(Number(e.target.value))}
        aria-label="Preview volume"
      />
    </div>
  )
}
