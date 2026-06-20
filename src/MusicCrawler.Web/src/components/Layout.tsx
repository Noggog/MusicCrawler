import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import VolumeControl from './VolumeControl'
import MyceliumBackdrop from './MyceliumBackdrop'

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
  const { user } = useAuth()

  return (
    <div className="app">
      <MyceliumBackdrop />
      <header className="topbar">
        <div className="brand">Mycelium</div>
        <nav className="nav">
          <NavLink to="/" className={navClass} end>
            Discover
          </NavLink>
          <NavLink to="/artists" className={navClass}>
            Artists
          </NavLink>
          <NavLink to="/purchases" className={navClass}>
            Download
          </NavLink>
          <NavLink to="/cleanup" className={navClass}>
            Cleanup
          </NavLink>
          {/* Dev panel — shown only to DEV_USERNAMES users (Plex tag tooling + similarity debug). */}
          {user?.isDev && (
            <NavLink to="/dev" className={navClass}>
              Dev
            </NavLink>
          )}
        </nav>
        <VolumeControl />
        <AuthBox />
      </header>
      <main className="content">{children}</main>
    </div>
  )
}
