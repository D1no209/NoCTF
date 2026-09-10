import tailwindcss from '@tailwindcss/vite'

const environment = (
  globalThis as typeof globalThis & { process?: { env?: Record<string, string | undefined> } }
).process?.env
const apiProxyTarget = environment?.NUXT_API_PROXY_TARGET ?? 'http://localhost:5080'
const apiProxySecure = environment?.NUXT_API_PROXY_SECURE !== 'false'

export default defineNuxtConfig({
  ssr: false,
  modules: [(_options, nuxt) => {
    if (!nuxt.options.dev) {
      nuxt.hook('pages:extend', (pages) => {
        const index = pages.findIndex(page => page.path === '/__ui-check')
        if (index >= 0) pages.splice(index, 1)
      })
    }
  }],
  devtools: {
    enabled: true,
  },
  css: ['~/assets/css/main.css'],
  // Only shared primitives are auto-imported. Features explicitly compose views.
  components: [{ path: '~/components/ui', pathPrefix: false, extensions: ['vue'] }],
  app: {
    head: {
      script: [
        {
          // 首帧前恢复语言，SPA 挂载时即可使用正确语种。
          innerHTML:
            "try{const l=localStorage.getItem('noctf-locale')||(navigator.language.toLowerCase().startsWith('zh')?'zh-CN':'en');document.documentElement.lang=l}catch(e){}",
        },
        {
          // 首帧前恢复主题,避免闪烁;默认深色,与 useTheme 的 vueuse-color-scheme 键一致
          innerHTML:
            "try{if(localStorage.getItem('vueuse-color-scheme')!=='light')document.documentElement.classList.add('dark')}catch(e){}",
        },
      ],
    },
  },
  hooks: {
    'prerender:routes'({ routes }) {
      routes.clear()
    },
  },
  typescript: {
    strict: true,
    tsConfig: {
      compilerOptions: {
        noUnusedLocals: true,
        noUnusedParameters: true,
      },
    },
  },
  vite: {
    plugins: [tailwindcss()],
    server: {
      strictPort: true,
      proxy: {
        '/api': { target: apiProxyTarget, changeOrigin: true, secure: apiProxySecure },
        '/hubs': {
          target: apiProxyTarget,
          changeOrigin: true,
          secure: apiProxySecure,
          ws: true,
        },
        '/health': { target: apiProxyTarget, changeOrigin: true, secure: apiProxySecure },
      },
    },
  },
})
