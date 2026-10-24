import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

declare const process: { env: Record<string, string | undefined> }

// The API resolves tenant/realm from the Host header, so the proxy must preserve it (acme.localhost, admin.localhost).
export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    allowedHosts: ['.localhost'],
    proxy: { '/api': { target: process.env.HRMS_API_URL ?? 'http://localhost:5180', changeOrigin: false } },
  },
})
