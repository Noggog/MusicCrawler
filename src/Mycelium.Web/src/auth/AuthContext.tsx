import { createContext, useContext, useEffect, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getMe, login, logout } from '../api/auth'
import type { CurrentUser } from '../types'

interface AuthState {
  user: CurrentUser | null
  isLoading: boolean
  login: (returnUrl?: string) => void
  logout: () => void
}

const AuthContext = createContext<AuthState | undefined>(undefined)

// The app sits behind Authentik, so an unauthenticated visitor still has an SSO
// session at the IdP — we redirect straight into the OIDC flow, which bounces them
// back signed in without a visible login step. The sessionStorage flag breaks the
// loop if a redirect ever comes back still-unauthenticated (misconfig, IdP error,
// or a fresh logout), so we fall back to the manual "Log in" button instead.
const AUTO_LOGIN_ATTEMPTED = 'mc.autoLoginAttempted'

export function AuthProvider({ children }: { children: ReactNode }) {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['me'],
    queryFn: getMe,
    staleTime: 5 * 60 * 1000,
    retry: false,
  })

  const user = data ?? null

  useEffect(() => {
    if (isLoading) return
    if (user) {
      sessionStorage.removeItem(AUTO_LOGIN_ATTEMPTED)
      return
    }
    // Don't auto-redirect on a network/server error or if we already tried once.
    if (isError) return
    if (sessionStorage.getItem(AUTO_LOGIN_ATTEMPTED)) return
    sessionStorage.setItem(AUTO_LOGIN_ATTEMPTED, '1')
    login(window.location.pathname + window.location.search)
  }, [user, isLoading, isError])

  return (
    <AuthContext.Provider value={{ user, isLoading, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return ctx
}
