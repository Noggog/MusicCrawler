import { Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Home from './pages/Home'
import Artists from './pages/Artists'
import Related from './pages/Related'

export default function App() {
  return (
    <Layout>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/artists" element={<Artists />} />
        {/* Dev-only debug view for the Deezer similarity graph. */}
        {import.meta.env.DEV && <Route path="/related" element={<Related />} />}
      </Routes>
    </Layout>
  )
}
