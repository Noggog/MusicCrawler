import { useSyncExternalStore } from 'react'

// App-wide playback volume (0–1) for Deezer previews. Kept in a tiny external store so the slider in
// the topbar and every <audio> element stay in sync, and persisted so the choice survives reloads.
const KEY = 'mycelium.volume'
const DEFAULT = 0.7

function clamp(v: number): number {
  return Math.min(1, Math.max(0, v))
}

function read(): number {
  const raw = localStorage.getItem(KEY)
  const n = raw == null ? NaN : Number(raw)
  return Number.isFinite(n) ? clamp(n) : DEFAULT
}

let volume = read()
const listeners = new Set<() => void>()

export function getVolume(): number {
  return volume
}

export function setVolume(v: number): void {
  volume = clamp(v)
  localStorage.setItem(KEY, String(volume))
  listeners.forEach((l) => l())
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

export function useVolume(): number {
  return useSyncExternalStore(subscribe, getVolume, getVolume)
}
