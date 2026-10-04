import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, reactive, ref, shallowRef, watch } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { adminAuditSubjectPath, adminChallengePath, adminCompetitionPath, adminRouteId, adminRuntimeChallengePath, adminRuntimePath, adminRuntimeTeamPath, adminTeamPath, adminTemplatePath, adminUserPath, adminWorkspacePath, validAdminId } from '../app/features/admin/admin-navigation'
import { createLatestRequestGuard } from '../app/lib/latest-request'

const source = await Bun.file(new URL('../app/features/admin/useAdminDetailRoute.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const first = '00000001-0000-4000-8000-000000000001'
const second = '00000001-0000-4000-8000-000000000002'
const base = '/admin/platform/users'
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

async function harness(initial: string, load: (id: string, signal: AbortSignal) => Promise<{ id: string }>) {
  const router = createRouter({ history: createMemoryHistory(), routes: [{ path: `${base}/:userId?`, component: {} }] })
  await router.push(initial)
  const route = reactive({ params: router.currentRoute.value.params, query: router.currentRoute.value.query, hash: router.currentRoute.value.hash })
  const scope = effectScope()
  const dependencies = { computed, ref, shallowRef, watch, onScopeDispose, adminRouteId, validAdminId, createLatestRequestGuard,
    useRoute: () => route, useRouter: () => router, translate: (key: string) => key, describeMessage: (key: string) => ({ key }),
    parseApiError: (value: unknown) => ({ displayMessage: value instanceof Error ? value.message : 'failed' }) }
  const factory = new Function('deps', `const { ${Object.keys(dependencies).join(', ')} } = deps; ${compiled}; return useAdminDetailRoute;`)(dependencies)
  const state = scope.run(() => {
    watch(router.currentRoute, (value) => { Object.assign(route, { params: value.params, query: value.query, hash: value.hash }) }, { flush: 'sync' })
    return factory('userId', base, load)
  })!
  return { state, router, stop: () => scope.stop() }
}

describe('admin detail path selection', () => {
  test('direct navigation loads independently, closing preserves query filters and history reopens', async () => {
    const loaded: string[] = []
    const app = await harness(`${base}/${second}?filter=Bot`, async id => { loaded.push(id); return { id } })
    await drain()
    expect(app.state.data.value.id).toBe(second)
    expect(loaded).toEqual([second])
    app.state.close(false)
    await drain()
    expect(app.router.currentRoute.value.path).toBe(base)
    expect(app.router.currentRoute.value.query.filter).toBe('Bot')
    expect(app.state.data.value).toBeNull()
    app.router.back()
    await drain()
    expect(app.state.data.value.id).toBe(second)
    app.stop()
  })

  test('switching, closing and reopening the same ID rejects late results', async () => {
    const pending: Array<{ id: string; signal: AbortSignal; resolve: (value: { id: string }) => void }> = []
    const app = await harness(`${base}/${first}`, (id, signal) => new Promise(resolve => pending.push({ id, signal, resolve })))
    await app.state.select(second)
    expect(pending[0]!.signal.aborted).toBe(true)
    pending[1]!.resolve({ id: second })
    await drain()
    expect(app.state.data.value.id).toBe(second)
    pending[0]!.resolve({ id: first })
    await drain()
    expect(app.state.data.value.id).toBe(second)
    await app.state.select(first)
    app.state.close(false)
    await drain()
    await app.state.select(first)
    pending[2]!.resolve({ id: 'stale' })
    await drain()
    expect(app.state.data.value).toBeNull()
    pending[3]!.resolve({ id: first })
    await drain()
    expect(app.state.data.value.id).toBe(first)
    app.stop()
    expect(app.state.data.value).toBeNull()
  })

  test('invalid IDs and rejected detail reads keep an actionable error without stale data', async () => {
    let calls = 0
    const app = await harness(`${base}/invalid`, async () => { calls++; throw new Error('Forbidden') })
    expect(calls).toBe(0)
    expect(app.state.error.value).toEqual({ key: 'adminNavigation.invalidId' })
    expect(app.state.open.value).toBe(true)
    await app.state.select(first)
    await drain()
    expect(app.state.error.value).toBe('Forbidden')
    expect(app.state.data.value).toBeNull()
    app.stop()
  })
})

test('management links use entity subpaths and typed runtime/audit attribution', () => {
  const paths = [adminUserPath(first), adminTeamPath(first, second), adminRuntimePath(first, second),
    adminRuntimePath(null, first), adminCompetitionPath(first), adminChallengePath(first, second), adminTemplatePath(first)]
  for (const path of paths) expect(path).not.toContain('?')
  expect(adminUserPath(first)).toBe(`${base}/${first}`)
  expect(adminRuntimeTeamPath({ purpose: 'AwdpTarget', competitionId: first, sourceTeamId: second, teamId: null })).toBe(adminTeamPath(first, second))
  expect(adminRuntimeTeamPath({ purpose: 'TemplateTest', competitionId: first, sourceTeamId: second })).toBeUndefined()
  expect(adminRuntimeTeamPath({ purpose: 'Player', competitionId: first })).toBeUndefined()
  expect(adminRuntimeChallengePath({ challengeId: first })).toBe(adminTemplatePath(first))
  expect(adminAuditSubjectPath({ kind: 'UserAccountLifecycle', subjectId: first })).toBe(adminUserPath(first))
  expect(adminAuditSubjectPath({ kind: 'PlatformAdministration', subjectId: first })).toBeUndefined()
  expect(adminWorkspacePath(adminUserPath(first)!)).toBe(base)
  expect(adminWorkspacePath(adminTeamPath(first, second)!)).toBe(`/admin/competitions/${first}/teams`)
  expect(adminWorkspacePath(adminRuntimePath(first, second)!)).toBe(`/admin/competitions/${first}/runtimes`)
  expect(adminWorkspacePath(adminChallengePath(first, second)!)).toBe(adminChallengePath(first, second))
})
