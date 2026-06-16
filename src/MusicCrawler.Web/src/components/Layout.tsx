import type { ReactNode } from 'react'
import { NavLink } from 'react-router-dom'

const navClass = ({ isActive }: { isActive: boolean }) =>
  isActive ? 'nav-link active' : 'nav-link'

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
        </nav>
      </aside>
      <main className="content">{children}</main>
    </div>
  )
}
