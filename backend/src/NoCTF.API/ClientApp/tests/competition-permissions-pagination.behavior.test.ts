import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, proxyRefs, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'

const source = await Bun.file(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdPermissionsPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')
const factory = (deps: Record<string, unknown>) => new Function('deps',
  `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAdminCompetitionsByIdPermissionsPage;`)(deps)
const drain = async () => { await nextTick(); await Promise.resolve() }

async function harness() {
  const candidates = [{ id: 'owner', userName: 'Owner' }, ...Array.from({ length: 23 }, (_, index) => ({
    id: `user-${index + 1}`, userName: `User ${String(index + 1).padStart(2, '0')}`,
  }))]
  const permissions = { ownerId: 'owner', managerIds: ['user-2'], judgeIds: [], observerIds: [] }
  const writes: unknown[] = []
  let reads = 0
  let mount!: () => Promise<void>
  const scope = effectScope()
  const state = scope.run(() => factory({
    ref, computed, watch, proxyRefs, useOffsetPagination, X: {},
    useCompetitionAdmin: () => ({ competitionId: 'competition', canManagePermissions: ref(true), refresh: async () => {} }),
    useAuth: () => ({ user: ref({ userId: 'owner', userName: 'Owner' }) }),
    adminUserPath: (id: string) => `/admin/users/${id}`,
    onMounted: (callback: () => Promise<void>) => { mount = callback },
    adminGetCompetition: async () => ({ data: { permissions: structuredClone(permissions) } }),
    adminListCompetitionPermissionCandidates: async () => { reads++; return { data: { items: candidates } } },
    adminPatchCompetition: async (request: { body: { permissions: typeof permissions } }) => {
      const payload = JSON.parse(JSON.stringify(request))
      writes.push(payload)
      return { data: { permissions: payload.body.permissions } }
    },
    describeMessage: (key: string) => ({ key }), toast: { success: () => {} },
    toastWriteError: (error: unknown) => { throw error },
  })())!
  await mount()
  await drain()
  return { state, candidates, writes, reads: () => reads, stop: () => scope.stop() }
}

describe('competition permission candidate pagination', () => {
  test('makes every candidate reachable, excludes the owner and uses the shared page sizes without refetching', async () => {
    const app = await harness()
    try {
      const { state } = app
      expect(state.total.value).toBe(23)
      expect(state.pageLimit.value).toBe(10)
      expect(state.pageCount.value).toBe(3)
      expect(state.pageCandidates.value.map((candidate: { id: string }) => candidate.id)).toEqual(app.candidates.slice(1, 11).map(candidate => candidate.id))
      await state.loadPage(2)
      expect(state.pageCandidates.value.map((candidate: { id: string }) => candidate.id)).toEqual(app.candidates.slice(11, 21).map(candidate => candidate.id))
      await state.loadPage(3)
      expect(state.pageCandidates.value.map((candidate: { id: string }) => candidate.id)).toEqual(['user-21', 'user-22', 'user-23'])
      await state.setPageSize(20)
      expect(state.page.value).toBe(1)
      expect(state.pageCandidates.value).toHaveLength(20)
      expect(state.pageCount.value).toBe(2)
      await state.loadPage(2)
      expect(state.pageCandidates.value).toHaveLength(3)
      await state.setPageSize(50)
      expect(state.page.value).toBe(1)
      expect(state.pageCandidates.value).toHaveLength(23)
      expect(app.reads()).toBe(1)
    }
    finally { app.stop() }
  })

  test('searches the complete list and returns to the first page, including empty results', async () => {
    const app = await harness()
    try {
      const { state } = app
      await state.loadPage(3)
      state.search.value = ' USER 23 '
      await drain()
      expect(state.page.value).toBe(1)
      expect(state.total.value).toBe(1)
      expect(state.pageCandidates.value[0].id).toBe('user-23')
      state.search.value = 'missing'
      await drain()
      expect(state.total.value).toBe(0)
      expect(state.pageCount.value).toBe(1)
      expect(state.pageCandidates.value).toEqual([])
      state.search.value = ''
      await drain()
      expect(state.total.value).toBe(23)
      expect(state.pageCandidates.value).toHaveLength(10)
    }
    finally { app.stop() }
  })

  test('keeps assignments and names across pages and saves the full permission selection', async () => {
    const app = await harness()
    try {
      const { state } = app
      state.add('manager', 'user-1')
      await state.loadPage(3)
      state.add('judge', 'user-21')
      state.add('observer', 'user-23')
      expect(state.assigned('user-1')).toBeTrue()
      expect(state.candidateName('user-1')).toBe('User 01')
      expect(state.candidates.value).toHaveLength(24)
      state.add('observer', 'user-21')
      await state.save()
      await drain()
      expect(app.writes).toEqual([{ path: { competitionId: 'competition' }, body: { permissions: {
        ownerId: 'owner', managerIds: ['user-2', 'user-1'], judgeIds: ['user-21'], observerIds: ['user-23'],
      } } }])
      expect(state.page.value).toBe(3)
      await state.loadPage(1)
      expect(state.assigned('user-1')).toBeTrue()
      expect(state.candidateName('user-23')).toBe('User 23')
    }
    finally { app.stop() }
  })
})
