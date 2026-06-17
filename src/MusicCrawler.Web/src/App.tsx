import { Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Home from './pages/Home'
import Artists from './pages/Artists'
import Discover from './pages/Discover'
import Purchases from './pages/Purchases'
import Related from './pages/Related'

export default function App() {
  return (
    <Layout>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/artists" element={<Artists />} />
        <Route path="/discover" element={<Discover />} />
        <Route path="/purchases" element={<Purchases />} />
        {/* Dev-only debug view for the Deezer similarity graph. */}
        {import.meta.env.DEV && <Route path="/related" element={<Related />} />}
      </Routes>
    </Layout>
  )
}
