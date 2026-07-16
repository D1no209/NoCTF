import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig, loadEnv } from 'vite'
import { mockDataPlugin } from './src/mocks/mockDataPlugin'

// https://vite.dev/config/
export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, '.', '')
  const apiTarget = env.NOCTF_API_TARGET || 'http://127.0.0.1'
  const enableMocks = command === 'serve' && env.VITE_ENABLE_MOCKS === 'true'

  return {
    plugins: [
      vue(),
      tailwindcss(),
      // Mock data is opt-in and only served in development, never copied to dist.
      ...(enableMocks ? [mockDataPlugin()] : []),
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
