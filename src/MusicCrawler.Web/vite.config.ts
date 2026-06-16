import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The backend URL is injected by the Aspire AppHost via WithEnvironment("VITE_BACKEND_URL", ...).
// When running the SPA standalone, fall back to the backend's default dev HTTP endpoint.
const backendUrl = process.env.VITE_BACKEND_URL ?? 'http://localhost:59665'

// Aspire assigns a port via the PORT env var (WithHttpEndpoint(env: "PORT")).
const port = process.env.PORT ? Number(process.env.PORT) : 5173

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port,
    // Proxy "/api/*" to the backend so the browser stays same-origin in dev (no CORS needed).
    proxy: {
      '/api': {
        target: backendUrl,
        changeOrigin: true,
        secure: false,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
