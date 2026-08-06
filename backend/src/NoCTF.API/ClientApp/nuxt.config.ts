export default defineNuxtConfig({
  ssr: false,
  devtools: {
    enabled: true,
  },
  hooks: {
    'prerender:routes'({ routes }) {
      routes.clear()
    },
  },
  runtimeConfig: {
    public: {
      apiBase: '/api/v1',
    },
  },
  typescript: {
    strict: true,
  },
  vite: {
    server: {
      strictPort: true,
    },
  },
})
