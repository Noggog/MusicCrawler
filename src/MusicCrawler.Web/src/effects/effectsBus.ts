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

/** Called by MyceliumBackdrop for each layer on mount/unmount. */
export function registerSporeField(handle: SporeFieldHandle): () => void {
  fields.add(handle)
  return () => {
    fields.delete(handle)
  }
}

/** Scatter spores away from a point (cursor, swipe origin). */
export function disturbSpores(x: number, y: number, strength?: number) {
  for (const f of fields) f.disturb(x, y, strength)
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
