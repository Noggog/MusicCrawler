import { useEffect, useRef } from 'react'
import { createSporeField, type SporeFieldOptions } from '../effects/sporeField'
import { registerSporeField } from '../effects/effectsBus'

/**
 * Ambient floating-spore layers with parallax depth:
 *
 *  - "far"  — the calm, dense bulk, drifting BEHIND the content (z-index -1) so
 *             it reads as atmosphere rather than clutter.
 *  - "near" — a sparse handful of larger, faster spores IN FRONT of everything
 *             (z-index 100), softly blurred, to sell depth without being in your
 *             face.
 *
 * Both honour prefers-reduced-motion (single static frame) and pause while the
 * tab is hidden. Mount once near the app root; positioning lives in index.css.
 */
const FAR: SporeFieldOptions = {
  // Calm bulk. Nudged brighter than the near layer since it reads through the
  // scanline grid and translucent panels.
  alphaScale: 1.3,
}

const NEAR: SporeFieldOptions = {
  density: 2.2, // very few
  maxSpores: 22,
  speed: 16, // closer ⇒ faster parallax
  minRadius: 3,
  maxRadius: 7,
  flareChance: 0.025,
  alphaScale: 0.7,
}

export default function MyceliumBackdrop() {
  const farRef = useRef<HTMLCanvasElement>(null)
  const nearRef = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const farCanvas = farRef.current
    const nearCanvas = nearRef.current
    if (!farCanvas || !nearCanvas) return

    const fields = [
      createSporeField(farCanvas, FAR),
      createSporeField(nearCanvas, NEAR),
    ]
    const unregister = fields.map(registerSporeField)

    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)')

    const sync = () => {
      const dpr = window.devicePixelRatio || 1
      for (const f of fields) f.resize(window.innerWidth, window.innerHeight, dpr)
      if (reduceMotion.matches) for (const f of fields) f.renderStatic()
    }

    const onVisibility = () => {
      if (reduceMotion.matches) return
      for (const f of fields) (document.hidden ? f.stop() : f.start())
    }

    sync()
    if (!reduceMotion.matches && !document.hidden) for (const f of fields) f.start()

    window.addEventListener('resize', sync)
    document.addEventListener('visibilitychange', onVisibility)
    reduceMotion.addEventListener('change', onVisibility)

    return () => {
      window.removeEventListener('resize', sync)
      document.removeEventListener('visibilitychange', onVisibility)
      reduceMotion.removeEventListener('change', onVisibility)
      for (const off of unregister) off()
      for (const f of fields) f.destroy()
    }
  }, [])

  return (
    <>
      <canvas ref={farRef} className="spore-backdrop spore-far" aria-hidden="true" />
      <canvas ref={nearRef} className="spore-backdrop spore-near" aria-hidden="true" />
    </>
  )
}
