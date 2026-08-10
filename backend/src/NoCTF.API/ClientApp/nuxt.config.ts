import tailwindcss from '@tailwindcss/vite'

export default defineNuxtConfig({
  ssr: false,
  devtools: {
    enabled: true,
  },
  css: ['~/assets/css/main.css'],
  components: [{ path: '~/components', pathPrefix: false, extensions: ['vue'] }],
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
  },
  vite: {
    plugins: [tailwindcss()],
    server: {
      strictPort: true,
      proxy: {
        '/api': { target: 'http://localhost:5080', changeOrigin: true },
        '/hubs': {
          target: 'http://localhost:5080',
          changeOrigin: true,
          ws: true,
        },
        '/health': { target: 'http://localhost:5080', changeOrigin: true },
      },
    },
  },
})
