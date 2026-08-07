import tailwindcss from '@tailwindcss/vite'

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
        '/api': { target: 'http://localhost:5080', changeOrigin: true },
        '/hubs': { target: 'http://localhost:5080', changeOrigin: true, ws: true },
        '/health': { target: 'http://localhost:5080', changeOrigin: true },
      },
    },
  },
})
