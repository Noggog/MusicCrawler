import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

const navClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'nav-link active' : 'nav-link'

function AuthBox() {
  const { user, isLoading, login, logout } = useAuth()

  if (isLoading) {
    return <div className="auth-box"><span className="auth-name">…</span></div>
  }

  if (!user) {
    return (
      <div className="auth-box">
        <button className="auth-btn" onClick={() => login()}>Log in</button>
      </div>
    )
  }

  return (
    <div className="auth-box">
      <span className="auth-name" title={user.email ?? undefined}>
        {user.displayName ?? user.username ?? user.email ?? 'Signed in'}
      </span>
      <button className="auth-btn" onClick={() => logout()}>Log out</button>
    </div>
  )
}

export default function Layout({ children }: { children: ReactNode }) {
  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand">MusicCrawler</div>
        <nav className="nav">
          <NavLink to="/" className={navClass} end>
            Home
          </NavLink>
          <NavLink to="/artists" className={navClass}>
            Artists
          </NavLink>
          <NavLink to="/discover" className={navClass}>
            Discover
          </NavLink>
          <NavLink to="/purchases" className={navClass}>
            To Buy
          </NavLink>
          {/* Dev-only debug view for the similarity graph; compiled out of production builds. */}
          {import.meta.env.DEV && (
            <NavLink to="/related" className={navClass}>
              Related (dev)
            </NavLink>
          )}
        </nav>
        <AuthBox />
      </aside>
      <main className="content">{children}</main>
    </div>
  )
}
