import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, onScopeDispose, reactive, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'

const source = await Bun.file(new URL('../app/features/routes/admin/challenges/useAdminChallengesIndexPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const refresh = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 270)); await drain() }
const mine = { id: 'mine', title: 'My template', direction: 'Pwn' }
const shared = { id: 'shared', title: 'Shared template', direction: 'Web' }
const result = (onlyMine: boolean) => ({ data: { items: onlyMine ? [mine] : [mine, shared], total: onlyMine ? 1 : 50, directions: onlyMine ? ['Pwn'] : ['Pwn', 'Web'] } })

function harness(fetcher: (query: any) => Promise<any> = async query => result(query.onlyMine), query: Record<string, string> = {}) {
  const route = reactive({ path: '/admin/challenges', query })
  const user = ref({ userId: 'owner' })
  const calls: any[] = []
  const deps = {
    ref, computed, watch, markRaw, useOffsetPagination,
    Filter: {}, Plus: {}, UserRound: {}, AdminDateTimeComponent: {}, AdminGameModeBadgeComponent: {}, ChallengeTemplateCreateDialogComponent: {},
    directionKey: (value: string) => value.toLowerCase(), directionLabel: (value: string) => value,
    useAuth: () => ({ canOrganize: ref(true), user }), useRoute: () => route,
    useRouter: () => ({ replace: async (next: any) => { route.query = next.query } }),
    adminChallengeBankListTemplates: ({ query }: any) => { calls.push(query); return fetcher(query) },
    onMounted: (callback: () => void) => callback(), onBeforeUnmount: onScopeDispose, navigateTo: async () => {},
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAdminChallengesIndexPage;`)(deps)
  function mount() {
    const scope = effectScope()
    const state = scope.run(factory)!
    return { state, stop: () => scope.stop() }
  }
  return { mount, route, user, calls }
}

describe('challenge library ownership filter', () => {
  test('filters the full server list, resets pagination and combines the other filters', async () => {
    const app = harness()
    const page = app.mount()
    await drain()
    await page.state.loadPage(3)
    expect(app.calls.at(-1).offset).toBe(20)
    page.state.toggleOnlyMine()
    await nextTick()
    expect(page.state.loading.value).toBe(true)
    await refresh()
    expect(app.calls.at(-1)).toMatchObject({ onlyMine: true, offset: 0, limit: 10 })
    expect(page.state.page.value).toBe(1)
    expect(page.state.total.value).toBe(1)
    expect(page.state.templates.value).toEqual([mine])
    expect(page.state.directionOptions.value.map((item: any) => item.value)).toEqual(['Pwn'])
    expect(app.route.query.mine).toBe('1')
    page.state.search.value = ' My template '
    page.state.directionFilter.value = 'Pwn'
    page.state.includeDeleted.value = true
    await refresh()
    expect(app.calls.at(-1)).toMatchObject({ onlyMine: true, keyword: 'My template', direction: 'Pwn', includeDeleted: true, offset: 0 })
    page.state.toggleOnlyMine()
    await refresh()
    expect(app.calls.at(-1).onlyMine).toBe(false)
    expect(app.route.query.mine).toBeUndefined()
    expect(page.state.templates.value).toEqual([mine, shared])
    page.stop()
  })

  test('ignores an old all-templates response after switching to only mine', async () => {
    let resolveAll!: (value: any) => void
    const app = harness(query => query.onlyMine ? Promise.resolve(result(true)) : new Promise(resolve => { resolveAll = resolve }))
    const page = app.mount()
    page.state.toggleOnlyMine()
    await refresh()
    resolveAll(result(false))
    await drain()
    expect(page.state.templates.value).toEqual([mine])
    expect(page.state.total.value).toBe(1)
    expect(page.state.directionOptions.value.map((item: any) => item.value)).toEqual(['Pwn'])
    page.stop()
    const restored = app.mount()
    expect(restored.state.templates.value).toEqual([mine])
    expect(restored.state.onlyMine.value).toBe(true)
    await drain()
    restored.stop()
  })

  test('restores the own filter and snapshot only for the same account', async () => {
    const app = harness(undefined, { mine: '1' })
    const page = app.mount()
    await drain()
    expect(app.calls[0].onlyMine).toBe(true)
    page.stop()
    const restored = app.mount()
    expect(restored.state.templates.value).toEqual([mine])
    expect(restored.state.onlyMine.value).toBe(true)
    await drain()
    restored.stop()
    app.user.value = { userId: 'another-owner' }
    app.route.query = {}
    const another = app.mount()
    expect(another.state.templates.value).toEqual([])
    expect(another.state.onlyMine.value).toBe(false)
    await drain()
    expect(app.calls.at(-1).onlyMine).toBe(false)
    another.stop()
  })
})
