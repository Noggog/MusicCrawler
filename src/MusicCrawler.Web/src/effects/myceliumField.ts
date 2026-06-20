/* ============================================================================
   Mycelium growth field — living hyphae for the Mycelium UI backdrop.

   A space-colonization growth simulation, self-contained and React-free in the
   same spirit as sporeField.ts. "Colonies" of glowing strands grow from a seed
   point out toward invisible nutrient attractors, branching organically; the
   growing tips are brightest. When a colony exhausts its nutrients it dwells,
   then fades and retracts, and a fresh colony sprouts elsewhere — so the field
   slowly breathes across the viewport rather than filling up and freezing.

   This is the EXPERIMENTAL engine: the constants below are tuning knobs meant
   to be played with to find a look worth refining. Mounting / resize /
   reduced-motion glue lives in components/MyceliumBackdrop.tsx.

   Algorithm (space colonization, à la Runions et al.):
     - scatter nutrient attractors in a soft cluster
     - each growth tick, every attractor pulls the nearest node within its
       influence radius; nodes step a fixed length toward the summed pull,
       spawning a child node (and branching where several attractors tug)
     - an attractor is consumed once a node gets within the kill radius
   ============================================================================ */

/** An RGB triple, 0-255. */
type Rgb = readonly [number, number, number]

/**
 * Warm-only palette, matched to sporeField — salmon tones + the peachy accent.
 * No blues/purples, per the design constraint.
 */
const PALETTE: readonly Rgb[] = [
  [250, 115, 118], // salmon-light
  [255, 153, 155], // salmon-white
  [225, 96, 99], // salmon
  [247, 185, 146], // yellow (peachy accent)
]

/** Fallback flare colour (only used when no pulse is actually contributing). */
const WHITE: Rgb = [255, 255, 255]

/** One node in a colony's branching tree. */
interface Node {
  x: number
  y: number
  /** Index of the parent node in the colony, or -1 for the seed root. */
  parent: number
  /** Accumulated pull from attractors during the current growth tick. */
  dx: number
  dy: number
  pulls: number
  /** Strand thickness at this node, tapering toward the tips. */
  width: number
  /** Per-node brightness 0..1, drifting along the strand (a random walk from the
      root) so intensity varies organically across the colony rather than
      radiating predictably from the convergence point. */
  shade: number
  /** Number of children — 0 ⇒ a tip, ≥2 ⇒ a branch intersection (glows more). */
  children: number
}

interface Attractor {
  x: number
  y: number
}

interface Colony {
  nodes: Node[]
  attractors: Attractor[]
  color: Rgb
  /** Fixed per-colony opacity (no time fade) so the field has quiet variation
      but stays stable — what's drawn is what stays. */
  baseAlpha: number
}

/**
 * A transient colour flare that washes outward from a point (e.g. the cursor on
 * approve/reject), lighting strands as its wavefront passes over them so the
 * pulse appears to race down the colony lines. Expands and decays over its life.
 */
interface Pulse {
  x: number
  y: number
  /** Elapsed seconds and total lifetime. */
  t: number
  dur: number
  /** Wavefront expansion speed (px/sec) and its gaussian thickness (px). */
  speed: number
  band: number
  /** Peak added brightness and the flare colour. */
  strength: number
  color: Rgb
}

const DEFAULTS = {
  /** Length of each grown segment, CSS px. */
  segment: 7,
  /** An attractor steers nodes within this radius. */
  attractRadius: 110,
  /** An attractor is consumed when a node gets this close. */
  killRadius: 18,
  /** Random angular jitter (radians) added to each growth step. */
  jitter: 0.32,
  /** How many colonies to build — they're grown once at load and then left
      alone (no new colonies, no fade-out). More ⇒ fuller page coverage. */
  colonies: 16,
  /** Base strand opacity (before per-colony / toward-tip variation + alphaScale). */
  strandAlpha: 0.5,
  /** Overall opacity multiplier for the layer. */
  alphaScale: 1,
  /** Cursor glow: strands/branches within this radius of the pointer brighten. */
  pointerRadius: 150,
  /** Cursor glow strength — kept low so it's a faint breath, not a spotlight. */
  pointerGlow: 0.5,
  /** Glow at branch intersections under the cursor (where strands split); only
      visible near the pointer, a touch brighter than the strand glow so forks
      pick up the light without becoming bright dots. */
  branchGlow: 0.8,
  /** Approve/reject flare: wavefront speed (px/sec), lifetime (s), band width
      (px) and peak brightness. Wide band + modest strength keep it a soft wash
      rather than a crisp expanding ring. Reaches ~speed×dur px from its origin. */
  pulseSpeed: 480,
  pulseDur: 2.1,
  pulseBand: 170,
  pulseStrength: 0.26,
  /** Max device-pixel-ratio honoured (perf guard on hi-dpi screens). */
  maxDpr: 2,
}

export type MyceliumFieldOptions = Partial<typeof DEFAULTS>

export interface MyceliumFieldHandle {
  resize: (w: number, h: number, dpr: number) => void
  start: () => void
  stop: () => void
  destroy: () => void
  /** Render a single static (fully-grown) frame for prefers-reduced-motion. */
  renderStatic: () => void
  /** Track the cursor so nearby strands/branches glow faintly. */
  setPointer: (x: number, y: number) => void
  /** Cursor left the window — fade the glow out. */
  clearPointer: () => void
  /** Fire a colour flare that races outward from a point along the strands. */
  pulse: (x: number, y: number, color?: Rgb) => void
}

/**
 * Build a mycelium growth field bound to a canvas. As with sporeField, the
 * caller owns sizing and lifecycle.
 */
export function createMyceliumField(
  canvas: HTMLCanvasElement,
  options: MyceliumFieldOptions = {},
): MyceliumFieldHandle {
  const CONFIG = { ...DEFAULTS, ...options }
  const ctx = canvas.getContext('2d', { alpha: true })!

  let width = 0 // CSS pixels
  let height = 0
  let dpr = 1
  let colonies: Colony[] = []
  let pulses: Pulse[] = []
  let raf = 0
  let last = 0
  let running = false

  // Cursor "nutrient light": strands near the pointer glow a little. `glow`
  // eases the effect in/out so entering/leaving the window isn't abrupt.
  let pointerX = 0
  let pointerY = 0
  let pointerActive = false
  let glow = 0
  // The field is static, so we only repaint while the cursor glow is animating.
  // `idle` marks that the last settled frame is already on the canvas.
  let idle = false

  const rand = (min: number, max: number) => min + Math.random() * (max - min)
  const pick = <T>(arr: readonly T[]): T => arr[(Math.random() * arr.length) | 0]

  /**
   * Spawn a colony: a soft elliptical cluster of attractors with its seed root
   * planted at the cluster's edge, so growth fans inward and sweeps across the
   * region rather than radiating symmetrically.
   */
  function spawnColony(): Colony {
    const span = Math.min(width, height)
    const clusterR = rand(0.16, 0.36) * span
    const cx = rand(clusterR * 0.3, width - clusterR * 0.3)
    const cy = rand(clusterR * 0.3, height - clusterR * 0.3)

    // Nutrient density scales with cluster area; clamp for sanity.
    const area = Math.PI * clusterR * clusterR
    const count = Math.max(40, Math.min(180, Math.round(area / 1200)))
    const squash = rand(0.6, 1) // slight ellipse so colonies aren't all round
    const tilt = rand(0, Math.PI)
    const cosT = Math.cos(tilt)
    const sinT = Math.sin(tilt)

    const attractors: Attractor[] = []
    for (let i = 0; i < count; i++) {
      // Area-uniform (sqrt) so nutrients fill the whole cluster — including near
      // the rim, which keeps the seed within reach and lets the strands branch
      // out fully instead of stalling as a single stroke.
      const r = clusterR * Math.sqrt(Math.random())
      const a = Math.random() * Math.PI * 2
      const ex = Math.cos(a) * r
      const ey = Math.sin(a) * r * squash
      attractors.push({
        x: cx + ex * cosT - ey * sinT,
        y: cy + ex * sinT + ey * cosT,
      })
    }

    // Seed root on the cluster rim, opposite a random direction, so growth fans
    // inward across the colony.
    const seedAngle = rand(0, Math.PI * 2)
    const root: Node = {
      x: cx + Math.cos(seedAngle) * clusterR,
      y: cy + Math.sin(seedAngle) * clusterR,
      parent: -1,
      dx: 0,
      dy: 0,
      pulls: 0,
      width: rand(1.6, 2.4),
      shade: rand(0.45, 1),
      children: 0,
    }

    // Guarantee the colony can bootstrap: if the nearest attractor is out of the
    // seed's attraction radius (common with large clusters), slide the root in
    // along that line until it's within reach. Without this the seed feels no
    // pull and the colony never grows past its root.
    let nd2 = Infinity
    let nax = root.x
    let nay = root.y
    for (const at of attractors) {
      const dx = at.x - root.x
      const dy = at.y - root.y
      const d2 = dx * dx + dy * dy
      if (d2 < nd2) {
        nd2 = d2
        nax = at.x
        nay = at.y
      }
    }
    const nd = Math.sqrt(nd2)
    const reach = CONFIG.attractRadius * 0.85
    if (nd > reach) {
      const t = (nd - reach) / nd
      root.x += (nax - root.x) * t
      root.y += (nay - root.y) * t
    }

    return {
      nodes: [root],
      attractors,
      color: pick(PALETTE),
      baseAlpha: rand(0.6, 1),
    }
  }

  /**
   * One space-colonization iteration for a colony: returns the number of nodes
   * added (0 means growth has stalled).
   */
  function growOnce(c: Colony): number {
    if (!c.attractors.length) return 0
    const { attractRadius, killRadius, segment, jitter } = CONFIG
    const aR2 = attractRadius * attractRadius
    const kR2 = killRadius * killRadius
    const nodes = c.nodes

    // Reset per-tick pull accumulators.
    for (const n of nodes) {
      n.dx = 0
      n.dy = 0
      n.pulls = 0
    }

    // Each attractor pulls only its single nearest node within range.
    for (const at of c.attractors) {
      let best = -1
      let bestD2 = aR2
      for (let i = 0; i < nodes.length; i++) {
        const dx = at.x - nodes[i].x
        const dy = at.y - nodes[i].y
        const d2 = dx * dx + dy * dy
        if (d2 < bestD2) {
          bestD2 = d2
          best = i
        }
      }
      if (best >= 0) {
        const n = nodes[best]
        const dx = at.x - n.x
        const dy = at.y - n.y
        const inv = 1 / Math.sqrt(dx * dx + dy * dy || 1)
        n.dx += dx * inv
        n.dy += dy * inv
        n.pulls++
      }
    }

    // Grow a child from every node that felt a pull this tick.
    let added = 0
    const parentCount = nodes.length
    for (let i = 0; i < parentCount; i++) {
      const n = nodes[i]
      if (n.pulls === 0) continue
      const inv = 1 / Math.sqrt(n.dx * n.dx + n.dy * n.dy || 1)
      let ang = Math.atan2(n.dy * inv, n.dx * inv) + rand(-jitter, jitter)
      const nx = n.x + Math.cos(ang) * segment
      const ny = n.y + Math.sin(ang) * segment
      nodes.push({
        x: nx,
        y: ny,
        parent: i,
        dx: 0,
        dy: 0,
        pulls: 0,
        // Taper toward tips, but never vanish.
        width: Math.max(0.45, n.width * 0.94),
        // Drift brightness off the parent so it varies smoothly along strands.
        shade: Math.max(0.3, Math.min(1, n.shade + rand(-0.13, 0.13))),
        children: 0,
      })
      n.children++
      added++
    }

    // Consume attractors that have been reached by any node.
    if (added) {
      c.attractors = c.attractors.filter((at) => {
        for (const n of nodes) {
          const dx = at.x - n.x
          const dy = at.y - n.y
          if (dx * dx + dy * dy < kR2) return false
        }
        return true
      })
    }

    return added
  }

  /** Ease the cursor-glow envelope and age any active flare pulses. */
  function step(dt: number) {
    const target = pointerActive ? 1 : 0
    glow += (target - glow) * Math.min(1, dt * 6)
    if (glow < 0.0005) glow = 0
    if (pulses.length) {
      for (const p of pulses) p.t += dt
      pulses = pulses.filter((p) => p.t < p.dur)
    }
  }

  /**
   * Total flare brightness at a point from all active pulses, and the colour of
   * the strongest contributor. Each pulse is a gaussian ring expanding from its
   * origin, fading over its lifetime.
   */
  function pulseAt(x: number, y: number): { a: number; color: Rgb } {
    let a = 0
    let color = WHITE
    let best = 0
    for (const p of pulses) {
      const dx = x - p.x
      const dy = y - p.y
      const d = Math.sqrt(dx * dx + dy * dy)
      const front = p.speed * p.t
      const dd = d - front
      // Asymmetric, soft profile rather than a crisp ring: a gentle leading edge
      // ahead of the front, and a long trailing wash filling everything behind it
      // — so the expanding shape never reads as a visible donut.
      let c: number
      if (dd >= 0) c = Math.exp(-(dd * dd) / (p.band * p.band))
      else c = Math.exp(dd / (p.band * 3.5))
      const env = 1 - p.t / p.dur // overall fade-out
      c *= p.strength * env
      if (c <= 0.0008) continue
      a += c
      if (c > best) {
        best = c
        color = p.color
      }
    }
    return { a, color }
  }

  /** Pointer proximity 0..1 at a point, already scaled by the eased `glow`. */
  function pointerAt(x: number, y: number): number {
    if (glow <= 0) return 0
    const dx = x - pointerX
    const dy = y - pointerY
    const d2 = dx * dx + dy * dy
    const r = CONFIG.pointerRadius
    if (d2 >= r * r) return 0
    const f = 1 - Math.sqrt(d2) / r
    return f * f * glow // quadratic falloff — tight, soft-edged pool of light
  }

  function draw() {
    ctx.clearRect(0, 0, width, height)
    ctx.globalCompositeOperation = 'lighter'
    ctx.lineCap = 'round'

    const { strandAlpha, alphaScale, pointerGlow, branchGlow } = CONFIG

    for (const c of colonies) {
      const [cr, cg, cb] = c.color
      const base = strandAlpha * c.baseAlpha * alphaScale
      const nodes = c.nodes

      // Strands: flat warm fibre whose brightness drifts randomly along each
      // strand (see node.shade), so the variation reads as organic rather than
      // radiating from the root. A faint cursor glow lifts segments near it, and
      // an approve/reject flare washes a coloured wavefront over them.
      const colStr = `rgba(${cr},${cg},${cb},1)`
      const havePulse = pulses.length > 0
      for (let i = 1; i < nodes.length; i++) {
        const n = nodes[i]
        const p = nodes[n.parent]
        const mx = (n.x + p.x) * 0.5
        const my = (n.y + p.y) * 0.5
        const lit = pointerAt(mx, my) * pointerGlow
        const a = base * n.shade + lit

        if (a > 0.003) {
          ctx.strokeStyle = colStr
          ctx.globalAlpha = Math.min(1, a)
          ctx.lineWidth = n.width
          ctx.beginPath()
          ctx.moveTo(p.x, p.y)
          ctx.lineTo(n.x, n.y)
          ctx.stroke()
        }

        // Flare overlay: an additive coloured stroke where a pulse front sits.
        if (havePulse) {
          const f = pulseAt(mx, my)
          if (f.a > 0.02) {
            ctx.strokeStyle = `rgba(${f.color[0]},${f.color[1]},${f.color[2]},${Math.min(1, f.a)})`
            ctx.globalAlpha = 1
            ctx.lineWidth = n.width + 0.2
            ctx.beginPath()
            ctx.moveTo(p.x, p.y)
            ctx.lineTo(n.x, n.y)
            ctx.stroke()
          }
        }
      }
      ctx.globalAlpha = 1

      // Branch intersections: a small soft bloom where strands split — shown
      // ONLY near the cursor (nothing when the pointer is away), brighter than
      // the strand glow so the forks pick up the light.
      if (glow <= 0) continue
      for (let i = 0; i < nodes.length; i++) {
        const n = nodes[i]
        if (n.children < 2) continue
        const a = pointerAt(n.x, n.y) * branchGlow * alphaScale
        if (a <= 0.01) continue
        const halo = 2 + n.width * 1.3
        const grad = ctx.createRadialGradient(n.x, n.y, 0, n.x, n.y, halo)
        grad.addColorStop(0, `rgba(${cr},${cg},${cb},${Math.min(1, a)})`)
        grad.addColorStop(1, `rgba(${cr},${cg},${cb},0)`)
        ctx.fillStyle = grad
        ctx.beginPath()
        ctx.arc(n.x, n.y, halo, 0, Math.PI * 2)
        ctx.fill()
      }
    }

    ctx.globalCompositeOperation = 'source-over'
  }

  function frame(now: number) {
    if (!running) return
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 0.016
    last = now
    step(dt)
    // The geometry never changes, so only repaint while the cursor glow or a
    // flare pulse is alive; once everything settles, draw one last frame and
    // idle until the next interaction.
    if (pointerActive || glow > 0 || pulses.length) {
      draw()
      idle = false
    } else if (!idle) {
      draw()
      idle = true
    }
    raf = requestAnimationFrame(frame)
  }

  /** Grow a colony out until its nutrients are exhausted (or a guard trips). */
  function growToCompletion(c: Colony) {
    let guard = 0
    while (c.attractors.length && guard < 1500) {
      if (growOnce(c) === 0) break
      guard++
    }
  }

  /**
   * Build the field once: every colony grown to completion synchronously, then
   * left untouched. No live growth, no fade, no new colonies — what's built here
   * is what stays on screen. Attractors are dropped afterwards (growth is done).
   */
  function seedColonies() {
    colonies = []
    for (let i = 0; i < CONFIG.colonies; i++) {
      const c = spawnColony()
      growToCompletion(c)
      c.attractors = []
      colonies.push(c)
    }
  }

  function resize(w: number, h: number, nextDpr: number) {
    // Reflow the existing field to the new viewport instead of regenerating it:
    // scale every node/attractor by the size delta so the SAME colonies stretch
    // to fit rather than being randomized and redrawn on each resize event.
    const sx = width ? w / width : 1
    const sy = height ? h / height : 1
    width = w
    height = h
    dpr = Math.min(CONFIG.maxDpr, nextDpr)
    canvas.width = Math.round(w * dpr)
    canvas.height = Math.round(h * dpr)
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)

    if (colonies.length) {
      if (sx !== 1 || sy !== 1) {
        for (const c of colonies) {
          for (const n of c.nodes) {
            n.x *= sx
            n.y *= sy
          }
        }
      }
      // The canvas was cleared by the resize; repaint the (now reflowed) field.
      draw()
      idle = false
    } else {
      // First sizing (or a destroyed field): build the field once.
      seedColonies()
      draw()
      idle = false
    }
  }

  function start() {
    if (running) return
    if (!width || !height) return
    if (!colonies.length) seedColonies()
    running = true
    last = 0
    idle = false
    raf = requestAnimationFrame(frame)
  }

  function stop() {
    running = false
    if (raf) cancelAnimationFrame(raf)
    raf = 0
  }

  function renderStatic() {
    // Reduced-motion: a full, settled field grown synchronously, drawn once —
    // no animation loop, no cursor glow.
    if (!width || !height) return
    seedColonies()
    draw()
  }

  function setPointer(x: number, y: number) {
    pointerX = x
    pointerY = y
    pointerActive = true
  }

  function clearPointer() {
    pointerActive = false
  }

  /** Fire a colour flare that races outward from (x, y) along the strands. */
  function pulse(x: number, y: number, color?: Rgb) {
    if (!running) return // no-op under reduced-motion / while stopped
    pulses.push({
      x,
      y,
      t: 0,
      dur: CONFIG.pulseDur,
      speed: CONFIG.pulseSpeed,
      band: CONFIG.pulseBand,
      strength: CONFIG.pulseStrength,
      color: color ?? WHITE,
    })
    if (pulses.length > 6) pulses.shift() // bound if spammed
    idle = false
  }

  function destroy() {
    stop()
    colonies = []
    pulses = []
  }

  return {
    resize,
    start,
    stop,
    destroy,
    renderStatic,
    setPointer,
    clearPointer,
    pulse,
  }
}
