import { expect, test } from 'bun:test'
import { computed, ref } from 'vue'

const source = await Bun.file(new URL('../app/features/routes/admin/platform/useAdminPlatformIndexPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')

test('basic configuration loads and saves the canonical public origin', async () => {
  let mounted: (() => void) | undefined
  const patches: unknown[] = []
  const branding = { name: 'NoCTF', description: null, publicBaseUrl: 'https://old.example', logoUrl: null }
  const dependencies = {
    computed, ref, Upload: () => null,
    onMounted: (callback: () => void) => { mounted = callback },
    usePlatform: () => ({ configuration: ref({ name: 'NoCTF' }) }),
    toast: { success: () => {}, error: () => {} }, describeMessage: (key: string) => key,
    parseApiError: (error: unknown) => ({ displayMessage: error }),
    adminPlatformGetInformation: async () => ({ data: {} }),
    adminPlatformGetConfiguration: async () => ({ data: { branding } }),
    adminPlatformPatchConfiguration: async (input: unknown) => { patches.push(input); return { data: { branding } } },
  }
  const factory = new Function('dependencies', `const { ${Object.keys(dependencies).join(', ')} } = dependencies; ${compiled}; return useAdminPlatformIndexPage`)(dependencies)
  const state = factory()
  mounted!()
  await new Promise(resolve => setTimeout(resolve, 0))
  expect(state.publicBaseUrl.value).toBe('https://old.example')
  state.publicBaseUrl.value = ' https://new.example '
  await state.save()
  expect(patches).toEqual([{ body: { branding: { name: 'NoCTF', description: null, publicBaseUrl: 'https://new.example' } } }])
})
