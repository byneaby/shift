import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://192.168.1.200:5080',
      '/health': 'http://192.168.1.200:5080',
      '/media': 'http://192.168.1.200:5080',
      '/hubs': {
        target: 'http://192.168.1.200:5080',
        ws: true,
      },
    },
  },
})
