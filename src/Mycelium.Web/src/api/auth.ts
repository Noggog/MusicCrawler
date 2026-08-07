import type { CurrentUser } from '../types'

// Current session from the BFF. The backend answers 401 (not a redirect) when signed out,
// which we surface as null rather than an error.
export async function getMe(): Promise<CurrentUser | null> {
  const res = await fetch('/auth/me')
  if (res.status === 401) return null
  if (!res.ok) {
    throw new Error(`Failed to load session: ${res.status} ${res.statusText}`)
  }
  return (await res.json()) as CurrentUser
}

// Login/logout are full-page navigations (not fetch): the BFF performs the OIDC redirect dance,
// so the browser must actually leave the SPA and come back.
export function login(returnUrl: string = window.location.pathname): void {
  window.location.href = `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`
}

export function logout(): void {
  // Mark that we deliberately signed out so the auto-login redirect doesn't fire
  // the moment we land back unauthenticated (see AuthContext). The user gets the
  // manual "Log in" button instead.
  sessionStorage.setItem('mc.autoLoginAttempted', '1')
  window.location.href = '/auth/logout'
}
