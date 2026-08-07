/* ============================================================================
   Audio-reactive glue — energises the spore field while a Deezer preview plays.

   We deliberately do NOT tap the <audio> element through Web Audio: Deezer's
   preview MP3s are served cross-origin, and routing a cross-origin media element
   through createMediaElementSource taints it and makes the browser output
   silence (and adding crossorigin="anonymous" would break loading when the CDN
   sends no CORS headers). So instead of a real spectrum we drive a lively
   "breathing" pulse, scaled by the current volume, that lifts the spores'
   glow + drift while something is playing and settles back when it stops.
   ============================================================================ */

import { setSporeIntensity } from './effectsBus'
import { getVolume } from '../audio/volume'

let raf = 0
let active = false
let startedAt = 0

function loop(now: number) {
  if (!active) {
    raf = 0
    return
  }
  const t = (now - startedAt) / 1000
  // A strong base lift with several out-of-phase sines so it never settles into
  // an obvious rhythm — reads as "music is on" rather than a metronome.
  const pulse =
    0.6 + 0.26 * Math.sin(t * 5.0) + 0.16 * Math.sin(t * 1.7 + 1) + 0.1 * Math.sin(t * 9.3)
  // Keep it apparent even at a low volume — volume modulates but never kills it.
  const vol = 0.55 + 0.45 * getVolume()
  setSporeIntensity(Math.max(0, Math.min(1, pulse * vol)))
  raf = requestAnimationFrame(loop)
}

/** Start energising the field. Called from the play click. (el unused.) */
export function startAudioReactive(_el?: HTMLAudioElement) {
  if (!active) startedAt = performance.now()
  active = true
  if (!raf) raf = requestAnimationFrame(loop)
}

/** Stop and let the glow settle back. Called from onPause/onEnded. */
export function stopAudioReactive() {
  active = false
  setSporeIntensity(0)
}
