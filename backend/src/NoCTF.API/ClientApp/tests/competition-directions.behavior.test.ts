import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, ref } from 'vue'
import { isLucideIconName, normalizeLucideIconName } from '../app/lib/lucide-icon-name'
import names from '../app/lib/lucide-icon-names.json'
import { ApiError } from '../app/utils/api-error'

const source = await Bun.file(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdDirectionsPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
function harness(writable = true) {
  const saved: any[] = []
  const requests: Array<{ options: any; resolve: (value: any) => void }> = []
  const deps = { Plus: {}, Trash2: {}, RefreshCw: {}, computed, ref, onMounted: (callback: () => void) => callback(), onBeforeUnmount: onScopeDispose,
    useCompetitionAdmin: () => ({ competitionId: 'competition', canWrite: ref(writable) }),
    adminGetCompetitionDirections: (options: any) => new Promise(resolve => requests.push({ options, resolve })),
    adminListCompetitionChallenges: async () => ({ data: { items: [{ directionId: 'web' }] } }),
    adminSaveCompetitionDirections: async (options: any) => { saved.push(options); return { data: { items: options.body.items } } },
    isLucideIconName, normalizeLucideIconName, translate: (key: string) => key,
    parseApiError: () => new ApiError('failed'), toast: { success: () => {} } }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAdminCompetitionsByIdDirectionsPage;`)(deps)
  const scope = effectScope()
  const state = scope.run(() => factory())!
  return { state, saved, requests, stop: () => scope.stop() }
}
const row = (id = 'web', name = 'Web', icon = 'globe') => ({ id, name, icon })
describe('competition direction catalog', () => {
  test('validates real Lucide suffixes and keeps client and API catalogs aligned', async () => {
    expect(normalizeLucideIconName(' lucide:key-round ')).toBe('key-round')
    for (const value of ['globe', 'binary', 'key-round', 'network', 'server-cog']) expect(isLucideIconName(value)).toBe(true)
    for (const value of ['../../globe', 'constructor', '__proto__', '', 'not-a-lucide-icon']) expect(isLucideIconName(value)).toBe(false)
    const api = await Bun.file(new URL('../../../NoCTF.Application/Competitions/Directions/lucide-icons.txt', import.meta.url)).text()
    expect(api.split(/\r?\n/).filter(Boolean)).toEqual(names)
  })
  test('guards used directions and saves custom spelling with a normalized icon suffix', async () => {
    const app = harness()
    app.requests[0]!.resolve({ data: { items: [row(), row('unused', 'Misc', 'puzzle')] } })
    await drain()
    app.state.remove(app.state.items.value[0])
    expect(app.state.items.value).toHaveLength(2)
    app.state.items.value[1] = row('unused', '自定义 Research', 'lucide:network')
    await app.state.save()
    expect(app.saved[0].body.items[1]).toEqual(row('unused', '自定义 Research', 'network'))
    app.state.items.value[1].name = ' web '
    expect(app.state.canSave.value).toBe(false)
    app.state.items.value[1].name = 'Research'
    app.state.items.value[1].icon = 'fake-icon'
    expect(app.state.canSave.value).toBe(false)
    app.stop()
  })
  test('refresh and unmount invalidate earlier requests', async () => {
    const app = harness()
    const second = app.state.load()
    expect(app.requests[0]!.options.signal.aborted).toBe(true)
    app.requests[1]!.resolve({ data: { items: [row('new')] } })
    await second
    app.requests[0]!.resolve({ data: { items: [row('old')] } })
    await drain()
    expect(app.state.items.value[0].id).toBe('new')
    app.stop()
    expect(app.requests[1]!.options.signal.aborted).toBe(true)
  })
})
