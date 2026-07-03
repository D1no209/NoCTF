import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

const apiTarget = process.env.NOCTF_API_TARGET ?? 'http://127.0.0.1'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    tailwindcss(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    cors: true,
    proxy: {
      '/hubs': {
        target: apiTarget,
        ws: true,
        changeOrigin: true,
      },
      '/api': {
        target: apiTarget,
        changeOrigin: true,
      },
    },
  },
})
