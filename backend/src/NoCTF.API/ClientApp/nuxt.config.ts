import tailwindcss from '@tailwindcss/vite'
import type { ProxyOptions } from 'vite'

// 开发代理:记录代理错误(ws 断开、后端重启等),由 Vite 自带的错误处理器收尾响应。
const proxyErrorHandling: Pick<ProxyOptions, 'configure'> = {
  configure: (proxy) => {
    proxy.on('error', (error) => {
      console.warn(`[dev-proxy] ${(error as NodeJS.ErrnoException).code ?? 'ERROR'}: ${error.message}`)
    })
  },
}

export default defineNuxtConfig({
  ssr: false,
  devtools: {
    enabled: true,
  },
  css: ['~/assets/css/main.css'],
  components: [{ path: '~/components', pathPrefix: false, extensions: ['vue'] }],
  hooks: {
    'prerender:routes'({ routes }) {
      routes.clear()
    },
  },
  typescript: {
    strict: true,
  },
  vite: {
    plugins: [tailwindcss()],
    server: {
      strictPort: true,
      proxy: {
        '/api': { target: 'http://localhost:5080', changeOrigin: true, ...proxyErrorHandling },
        '/hubs': {
          target: 'http://localhost:5080',
          changeOrigin: true,
          ws: true,
          ...proxyErrorHandling,
        },
        '/health': { target: 'http://localhost:5080', changeOrigin: true, ...proxyErrorHandling },
      },
    },
  },
})
