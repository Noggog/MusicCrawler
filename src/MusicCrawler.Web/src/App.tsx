import { Navigate, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Artists from './pages/Artists'
import Discover from './pages/Discover'
import Purchases from './pages/Purchases'
import Dev from './pages/Dev'

export default function App() {
  return (
    <Layout>
      <Routes>
        <Route path="/" element={<Discover />} />
        {/* Discover used to live here; keep old links/bookmarks working. */}
        <Route path="/discover" element={<Navigate to="/" replace />} />
        <Route path="/artists" element={<Artists />} />
        {/* Ratings was folded into the Artists drill-down + Download queue; keep old links working. */}
        <Route path="/ratings" element={<Navigate to="/artists" replace />} />
        <Route path="/purchases" element={<Purchases />} />
        {/* Cleanup and the old similarity debugger were folded into the dev panel. Keep old links working. */}
        <Route path="/cleanup" element={<Navigate to="/dev" replace />} />
        <Route path="/related" element={<Navigate to="/dev" replace />} />
        {/* Dev panel (Plex tag tooling + similarity debug). Visible only to DEV_USERNAMES; the
            page itself gates on isDev and every endpoint re-checks server-side. */}
        <Route path="/dev" element={<Dev />} />
      </Routes>
    </Layout>
  )
}
