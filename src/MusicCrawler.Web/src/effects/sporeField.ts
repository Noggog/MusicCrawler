/* ============================================================================
   Spore field — ambient floating-particle layer for the Mycelium UI.

   A self-contained canvas-2D simulation, deliberately decoupled from React so
   the reactive hooks planned next (cursor as nutrient, audio-reactive bloom,
   approve/reject bursts) can drive it imperatively without re-rendering. Drift
   uses a cheap rotating flow field for organic, slime-mold-ish wander; spores
   are drawn as additive radial blooms in the Neon Synapse palette.

   This file is the engine only. Mounting, resize, reduced-motion and the React
   glue live in components/MyceliumBackdrop.tsx, and the imperative event API
   used by the rest of the app lives in effects/effectsBus.ts.
   ============================================================================ */

/** An RGB triple, 0-255. */
type Rgb = readonly [number, number, number]

/**
 * Palette pulled from index.css, weighted toward salmon by repetition so most
 * spores read warm with the odd cool cyan/purple accent.
 */
const PALETTE: readonly Rgb[] = [
  [250, 115, 118], // salmon-light
  [250, 115, 118], // salmon-light
  [255, 153, 155], // salmon-white
  [225, 96, 99], // salmon
  [87, 241, 252], // cyan (accent)
  [140, 118, 219], // purple-fg (accent)
]

/** One drifting spore. */
interface Spore {
  x: number
  y: number
  /** Depth 0 (far) .. 1 (near) — drives size, speed, alpha and parallax. */
  z: number
  vx: number
  vy: number
  radius: number
  color: Rgb
  /** Phase offsets so every spore twinkles / wanders independently. */
  twinkle: number
  wander: number
  /** 0..1 flare envelope; spikes occasionally so a spore "catches the light". */
  flare: number
  flareVel: number
}

/** Tunables. Overridable per-layer via createSporeField options. */
const DEFAULTS = {
  /** Spores per million CSS pixels of viewport, before the hard cap. */
  density: 21,
  maxSpores: 192,
  /** Base drift speed in px/sec at z=1. */
  speed: 8,
  /** Strength of the curl/flow field steering. */
  flow: 0.45,
  minRadius: 0.6,
  maxRadius: 3.4,
  /** Chance per second that an idle spore begins to glint. */
  flareChance: 0.035,
  /** Overall opacity multiplier for this layer. */
  alphaScale: 1,
  /** Max device-pixel-ratio we honour (perf guard on hi-dpi screens). */
  maxDpr: 2,
}

export type SporeFieldOptions = Partial<typeof DEFAULTS>

/**
 * Smooth, seamless flow field: returns an angle for a point in space/time.
 * Sum of a few sines — far cheaper than real Perlin and plenty organic for
 * ambient drift.
 */
function flowAngle(x: number, y: number, t: number): number {
  const a =
    Math.sin(x * 0.0016 + t * 0.07) +
    Math.cos(y * 0.0019 - t * 0.05) +
    Math.sin((x + y) * 0.0011 + t * 0.04)
  return a * Math.PI
}

export interface SporeFieldHandle {
  resize: (w: number, h: number, dpr: number) => void
  start: () => void
  stop: () => void
  destroy: () => void
  /** Render a single static frame (used for prefers-reduced-motion). */
  renderStatic: () => void
  /** --- Reactive API (wired up in later passes) --- */
  /** Disturb spores near a point, e.g. the cursor or a swipe origin. */
  disturb: (x: number, y: number, strength?: number) => void
  /** Spawn a burst of spores at a point, e.g. on "approve". */
  burst: (x: number, y: number, count?: number, color?: Rgb) => void
  /** Global intensity 0..1, e.g. audio level — scales glow + drift energy. */
  setIntensity: (level: number) => void
}

/**
 * Build a spore field bound to a canvas. The caller owns sizing (so it can read
 * layout / devicePixelRatio) and lifecycle.
 */
export function createSporeField(
  canvas: HTMLCanvasElement,
  options: SporeFieldOptions = {},
): SporeFieldHandle {
  const CONFIG = { ...DEFAULTS, ...options }
  const ctx = canvas.getContext('2d', { alpha: true })!

  let width = 0 // CSS pixels
  let height = 0
  let dpr = 1
  let spores: Spore[] = []
  let raf = 0
  let last = 0
  let running = false
  let intensity = 0
  let intensityTarget = 0

  const rand = (min: number, max: number) => min + Math.random() * (max - min)
  const pick = <T>(arr: readonly T[]): T => arr[(Math.random() * arr.length) | 0]

  function makeSpore(seedAcrossScreen: boolean): Spore {
    const z = Math.random()
    return {
      x: seedAcrossScreen ? Math.random() * width : 0,
      y: seedAcrossScreen ? Math.random() * height : 0,
      z,
      vx: 0,
      vy: 0,
      radius: CONFIG.minRadius + (CONFIG.maxRadius - CONFIG.minRadius) * z,
      color: pick(PALETTE),
      twinkle: Math.random() * Math.PI * 2,
      wander: Math.random() * 1000,
      flare: 0,
      flareVel: 0,
    }
  }

  function targetCount(): number {
    const area = (width * height) / 1_000_000
    return Math.min(CONFIG.maxSpores, Math.round(area * CONFIG.density))
  }

  function reseed() {
    const want = targetCount()
    if (spores.length > want) {
      spores.length = want
    } else {
      while (spores.length < want) spores.push(makeSpore(true))
    }
  }

  function resize(w: number, h: number, nextDpr: number) {
    width = w
    height = h
    dpr = Math.min(CONFIG.maxDpr, nextDpr)
    canvas.width = Math.round(w * dpr)
    canvas.height = Math.round(h * dpr)
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    reseed()
    if (!running) renderStatic()
  }

  function wrap(s: Spore) {
    const m = 40
    if (s.x < -m) s.x = width + m
    else if (s.x > width + m) s.x = -m
    if (s.y < -m) s.y = height + m
    else if (s.y > height + m) s.y = -m
  }

  function step(dt: number, t: number) {
    intensity += (intensityTarget - intensity) * Math.min(1, dt * 3)
    const energy = 1 + intensity * 0.8

    for (const s of spores) {
      const angle = flowAngle(s.x, s.y + s.wander, t)
      const sp = CONFIG.speed * (0.35 + s.z) * energy
      // Steer velocity toward the flow direction, keep a little inertia.
      const tx = Math.cos(angle) * sp
      const ty = Math.sin(angle) * sp - sp * 0.25 // slight upward bias: spores rise
      s.vx += (tx - s.vx) * CONFIG.flow * dt * 4
      s.vy += (ty - s.vy) * CONFIG.flow * dt * 4
      s.x += s.vx * dt
      s.y += s.vy * dt
      s.twinkle += dt * (0.35 + 0.35 * s.z)

      // Glint envelope: occasionally a spore catches the light — a slow, gentle
      // swell rather than a flash.
      if (s.flare <= 0 && s.flareVel <= 0) {
        if (Math.random() < CONFIG.flareChance * dt) s.flareVel = rand(0.4, 0.7)
      }
      s.flare += s.flareVel * dt
      if (s.flare >= 1) {
        s.flare = 1
        s.flareVel = -rand(0.25, 0.45)
      } else if (s.flare < 0) {
        s.flare = 0
        s.flareVel = 0
      }

      wrap(s)
    }
  }

  function draw() {
    ctx.clearRect(0, 0, width, height)
    ctx.globalCompositeOperation = 'lighter'
    const glowBoost = 1 + intensity * 0.6

    for (const s of spores) {
      const twinkle = 0.8 + 0.2 * Math.sin(s.twinkle)
      const r = s.radius * (1 + s.flare * 0.55) * glowBoost
      const alpha =
        (0.12 + 0.5 * s.z) * twinkle * (0.9 + 0.45 * s.flare) * 0.6 * CONFIG.alphaScale
      const [cr, cg, cb] = s.color
      const halo = r * 4.5
      const g = ctx.createRadialGradient(s.x, s.y, 0, s.x, s.y, halo)
      g.addColorStop(0, `rgba(${cr},${cg},${cb},${Math.min(1, alpha)})`)
      g.addColorStop(0.35, `rgba(${cr},${cg},${cb},${Math.min(1, alpha * 0.4)})`)
      g.addColorStop(1, `rgba(${cr},${cg},${cb},0)`)
      ctx.fillStyle = g
      ctx.beginPath()
      ctx.arc(s.x, s.y, halo, 0, Math.PI * 2)
      ctx.fill()

      // Bright core.
      ctx.fillStyle = `rgba(255,255,255,${Math.min(0.9, alpha * 1.6)})`
      ctx.beginPath()
      ctx.arc(s.x, s.y, Math.max(0.5, r * 0.45), 0, Math.PI * 2)
      ctx.fill()
    }
    ctx.globalCompositeOperation = 'source-over'
  }

  function frame(now: number) {
    if (!running) return
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 0.016
    last = now
    step(dt, now / 1000)
    draw()
    raf = requestAnimationFrame(frame)
  }

  function start() {
    if (running) return
    running = true
    last = 0
    raf = requestAnimationFrame(frame)
  }

  function stop() {
    running = false
    if (raf) cancelAnimationFrame(raf)
    raf = 0
  }

  function renderStatic() {
    // One settled frame for reduced-motion: nudge each spore once so the flow
    // field shapes the layout, then draw.
    for (const s of spores) {
      s.twinkle = Math.PI / 2 // mid-twinkle
    }
    draw()
  }

  function destroy() {
    stop()
    spores = []
  }

  // --- Reactive API (no-ops beyond simple physics until later passes) ---
  function disturb(x: number, y: number, strength = 1) {
    const radius = 140
    const r2 = radius * radius
    for (const s of spores) {
      const dx = s.x - x
      const dy = s.y - y
      const d2 = dx * dx + dy * dy
      if (d2 < r2 && d2 > 0.01) {
        const f = (1 - d2 / r2) * strength * 60
        const inv = 1 / Math.sqrt(d2)
        s.vx += dx * inv * f
        s.vy += dy * inv * f
      }
    }
  }

  function burst(x: number, y: number, count = 16, color?: Rgb) {
    for (let i = 0; i < count; i++) {
      const s = makeSpore(false)
      const a = Math.random() * Math.PI * 2
      const sp = rand(40, 160)
      s.x = x
      s.y = y
      s.vx = Math.cos(a) * sp
      s.vy = Math.sin(a) * sp
      s.flare = 1
      s.flareVel = -rand(0.5, 1)
      if (color) s.color = color
      spores.push(s)
    }
    // Keep the array bounded; drop the oldest excess.
    const cap = targetCount() + 120
    if (spores.length > cap) spores.splice(0, spores.length - cap)
  }

  function setIntensity(level: number) {
    intensityTarget = Math.max(0, Math.min(1, level))
  }

  return {
    resize,
    start,
    stop,
    destroy,
    renderStatic,
    disturb,
    burst,
    setIntensity,
  }
}
