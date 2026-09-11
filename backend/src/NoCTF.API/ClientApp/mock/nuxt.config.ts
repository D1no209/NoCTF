import { createResolver } from '@nuxt/kit'

const { resolve } = createResolver(import.meta.url)
const environment = (globalThis as typeof globalThis & { process?: { env?: Record<string, string | undefined> } }).process?.env

/** A separate Nuxt application reusing the production source as a layer. */
export default defineNuxtConfig({
  extends: ['..'],
  srcDir: resolve('../app'),
  buildDir: '.nuxt',
  components: [{ path: resolve('../app/components/ui'), pathPrefix: false, extensions: ['vue'] }],
  devtools: { enabled: false },
  plugins: environment?.NOCTF_MOCK_DIAGNOSTICS === '1' ? ['./diagnostics.client.ts'] : [],
  modules: ['@nuxtjs/turnstile', (_options, nuxt) => {
    // The app source is outside this Mock root. Refresh its component registry
    // when primitives are added/removed instead of retaining the layer scan cache.
    nuxt.hook('builder:watch', (event, path) => {
      if ((event === 'add' || event === 'unlink') && /(?:^|\/)components\/ui\/.*\.vue$/.test(path.replaceAll('\\', '/')))
        return nuxt.callHook('restart')
    })
  }],
  hooks: {
    'components:dirs'(directories) {
      directories.splice(0, directories.length, {
        path: resolve('../app/components/ui'), pathPrefix: false, extensions: ['vue'],
      })
    },
  },
  vite: {
    server: {
      // Browser Vue warnings must not serialize reactive graphs back through Vite.
      // Keep the original warnings in the browser console for diagnosis.
      forwardConsole: false,
      proxy: Object.fromEntries(['/api', '/hubs', '/health'].map(path => [path, {
        target: 'http://127.0.0.1:5081', changeOrigin: true, ws: false,
      }])),
    },
  },
})
