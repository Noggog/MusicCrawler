/* ============================================================================
   Effects bus — thin imperative bridge to the live spore field(s).

   Components anywhere in the tree (swipe cards, audio player, pointer handlers)
   can call these helpers to drive the backdrop without prop-drilling or context.
   The backdrop runs two parallax layers (far/near), so each helper broadcasts
   to every registered field. Calls are no-ops until a field registers, so
   importing and calling from anywhere is always safe.
   ============================================================================ */

import type { SporeFieldHandle } from './sporeField'

const fields = new Set<SporeFieldHandle>()

// Last known cursor position, kept so effects fired from a click (e.g. approve)
// can originate at the pointer without each caller threading coordinates.
let lastX = 0
let lastY = 0

/** Called by MyceliumBackdrop for each layer on mount/unmount. */
export function registerSporeField(handle: SporeFieldHandle): () => void {
  fields.add(handle)
  return () => {
    fields.delete(handle)
  }
}

/** Update the cursor "nutrient" position — spores are repelled from it. */
export function setSporePointer(x: number, y: number) {
  lastX = x
  lastY = y
  for (const f of fields) f.setPointer(x, y)
}

/** Cursor left the window — stop repulsion. */
export function clearSporePointer() {
  for (const f of fields) f.clearPointer()
}

/** Last known cursor position, e.g. for click-triggered bursts. */
export function lastPointer(): { x: number; y: number } {
  return { x: lastX, y: lastY }
}

/** Scatter spores away from a point (cursor, swipe origin). */
export function disturbSpores(x: number, y: number, strength?: number) {
  for (const f of fields) f.disturb(x, y, strength)
}

/** Swirl existing spores in a transient whirlpool around a point. */
export function vortex(x: number, y: number, strength?: number) {
  for (const f of fields) f.vortex(x, y, strength)
}

/** Puff a burst of spores at a point — e.g. on approve. */
export function burstSpores(
  x: number,
  y: number,
  count?: number,
  color?: readonly [number, number, number],
) {
  for (const f of fields) f.burst(x, y, count, color)
}

/** Set global glow/drift intensity 0..1 — e.g. audio level. */
export function setSporeIntensity(level: number) {
  for (const f of fields) f.setIntensity(level)
}
