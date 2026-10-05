import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Адрес сервера для режима разработки. На другой машине достаточно задать
// SHIFTCLUB_API_URL, править файл не нужно.
const apiUrl = process.env.SHIFTCLUB_API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': apiUrl,
      '/health': apiUrl,
      '/media': apiUrl,
      '/hubs': {
        target: apiUrl,
        ws: true,
      },
    },
  },
})
