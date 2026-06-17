import { Navigate, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Artists from './pages/Artists'
import Discover from './pages/Discover'
import Ratings from './pages/Ratings'
import Purchases from './pages/Purchases'
import Cleanup from './pages/Cleanup'
import Related from './pages/Related'

export default function App() {
  return (
    <Layout>
      <Routes>
        <Route path="/" element={<Discover />} />
        {/* Discover used to live here; keep old links/bookmarks working. */}
        <Route path="/discover" element={<Navigate to="/" replace />} />
        <Route path="/artists" element={<Artists />} />
        <Route path="/ratings" element={<Ratings />} />
        <Route path="/purchases" element={<Purchases />} />
        <Route path="/cleanup" element={<Cleanup />} />
        {/* Dev-only debug view for the Deezer similarity graph. */}
        {import.meta.env.DEV && <Route path="/related" element={<Related />} />}
      </Routes>
    </Layout>
  )
}
