import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'
import { mockDataPlugin } from './src/mocks/mockDataPlugin'

// https://vite.dev/config/
export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, '.', '')
  const apiTarget = env.NOCTF_API_TARGET || 'http://127.0.0.1'

  return {
    plugins: [
      vue(),
      tailwindcss(),
      // The route table is always served in development; the runtime decides
      // whether mocks apply (VITE_ENABLE_MOCKS=true, or automatic fallback when
      // the backend is unreachable). Never copied to dist.
      ...(command === 'serve' ? [mockDataPlugin()] : []),
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
  }
})
