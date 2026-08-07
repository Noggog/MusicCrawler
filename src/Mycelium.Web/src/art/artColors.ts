// Art-driven accent colours, à la Spotify / Plexamp: we pull a single vivid colour out of an album /
// artist image and let the cards theme themselves from it. Done entirely client-side — Deezer's image
// CDN serves `access-control-allow-origin: *`, so the browser can read pixels off a canvas (with
// crossOrigin = 'anonymous') without the backend ever touching the bytes.
//
// The exported `useArtAccent(url)` returns the accent as a bare `"r, g, b"` channel triple so CSS can
// compose it at any alpha: `rgba(var(--art-accent), 0.4)` for a glow, `rgb(var(--art-accent))` solid.
import { useEffect, useState } from 'react'

// The image is drawn into a tiny square before we read it back — a 28px thumbnail carries plenty of
// colour signal and keeps getImageData (and the per-pixel loop) cheap.
const SAMPLE_SIZE = 28

function rgbToHsl(r: number, g: number, b: number): [number, number, number] {
  r /= 255
  g /= 255
  b /= 255
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const l = (max + min) / 2
  const d = max - min
  let h = 0
  let s = 0
  if (d !== 0) {
    s = l > 0.5 ? d / (2 - max - min) : d / (max + min)
    switch (max) {
      case r:
        h = (g - b) / d + (g < b ? 6 : 0)
        break
      case g:
        h = (b - r) / d + 2
        break
      default:
        h = (r - g) / d + 4
    }
    h /= 6
  }
  return [h, s, l]
}

function hslToRgb(h: number, s: number, l: number): [number, number, number] {
  if (s === 0) {
    const v = l * 255
    return [v, v, v]
  }
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s
  const p = 2 * l - q
  const hue = (t: number) => {
    if (t < 0) t += 1
    if (t > 1) t -= 1
    if (t < 1 / 6) return p + (q - p) * 6 * t
    if (t < 1 / 2) return q
    if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6
    return p
  }
  return [hue(h + 1 / 3) * 255, hue(h) * 255, hue(h - 1 / 3) * 255]
}

// Nudge the winning colour into a band that reads as a clean glow on the dark "Synthesis" UI: a muddy
// or washed-out swatch is pushed toward enough saturation + mid lightness to be legible, while a
// genuinely greyscale image (B&W cover art) is kept neutral rather than hallucinating a hue.
function tuneForDarkUi(r: number, g: number, b: number): [number, number, number] {
  let [h, s, l] = rgbToHsl(r, g, b)
  if (s > 0.08) {
    s = Math.min(1, Math.max(s, 0.55))
    l = Math.min(0.62, Math.max(0.45, l))
  } else {
    l = Math.min(0.65, Math.max(0.5, l))
  }
  return hslToRgb(h, s, l)
}

function extractAccent(img: HTMLImageElement): string | null {
  const canvas = document.createElement('canvas')
  canvas.width = SAMPLE_SIZE
  canvas.height = SAMPLE_SIZE
  const ctx = canvas.getContext('2d', { willReadFrequently: true })
  if (!ctx) return null
  ctx.drawImage(img, 0, 0, SAMPLE_SIZE, SAMPLE_SIZE)

  let data: Uint8ClampedArray
  try {
    data = ctx.getImageData(0, 0, SAMPLE_SIZE, SAMPLE_SIZE).data
  } catch {
    // Tainted canvas — the image's host didn't allow cross-origin reads. Fall back to no accent.
    return null
  }

  // Bucket pixels into a coarse 12-bit colour cube and weight each by vividness, so a small splash of
  // vivid colour outvotes a large field of grey/black — that's what makes the accent feel "of the art"
  // rather than a muddy average. A small baseline weight keeps near-monochrome covers from yielding
  // nothing at all.
  const buckets = new Map<number, { r: number; g: number; b: number; w: number }>()
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] < 125) continue // skip transparent pixels
    const r = data[i]
    const g = data[i + 1]
    const b = data[i + 2]
    const [, s, l] = rgbToHsl(r, g, b)
    const vivid = s * (1 - Math.abs(l - 0.55))
    const w = 0.1 + vivid
    const key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4)
    const cur = buckets.get(key)
    if (cur) {
      cur.r += r * w
      cur.g += g * w
      cur.b += b * w
      cur.w += w
    } else {
      buckets.set(key, { r: r * w, g: g * w, b: b * w, w })
    }
  }
  if (buckets.size === 0) return null

  let best: { r: number; g: number; b: number; w: number } | null = null
  for (const v of buckets.values()) {
    if (!best || v.w > best.w) best = v
  }
  if (!best) return null

  const [r, g, b] = tuneForDarkUi(best.r / best.w, best.g / best.w, best.b / best.w)
  return `${Math.round(r)}, ${Math.round(g)}, ${Math.round(b)}`
}

function loadAccent(url: string): Promise<string | null> {
  return new Promise((resolve) => {
    const img = new Image()
    img.crossOrigin = 'anonymous'
    img.decoding = 'async'
    img.onload = () => resolve(extractAccent(img))
    img.onerror = () => resolve(null)
    img.src = url
  })
}

// Per-URL caches so each image is analysed at most once per session. `cache` stores the final result
// (a triple, or null for "analysed, no usable colour"); `inflight` dedupes concurrent requests for the
// same URL (e.g. a list row and its detail-pane twin asking at the same moment).
const cache = new Map<string, string | null>()
const inflight = new Map<string, Promise<string | null>>()

export function getArtAccent(url: string): Promise<string | null> {
  if (cache.has(url)) return Promise.resolve(cache.get(url) ?? null)
  let pending = inflight.get(url)
  if (!pending) {
    pending = loadAccent(url).then((accent) => {
      cache.set(url, accent)
      inflight.delete(url)
      return accent
    })
    inflight.set(url, pending)
  }
  return pending
}

// Resolve the accent for an image URL. Returns null while loading, on failure, or when no URL is
// given — callers gate the styling on a non-null value and otherwise keep the default theme.
export function useArtAccent(url: string | null | undefined): string | null {
  const [accent, setAccent] = useState<string | null>(() => (url ? cache.get(url) ?? null : null))
  useEffect(() => {
    if (!url) {
      setAccent(null)
      return
    }
    const known = cache.get(url)
    if (known !== undefined) {
      setAccent(known)
      return
    }
    let active = true
    getArtAccent(url).then((a) => {
      if (active) setAccent(a)
    })
    return () => {
      active = false
    }
  }, [url])
  return accent
}
