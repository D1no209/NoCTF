import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, proxyRefs, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { useCursorPagination } from '../app/composables/useCursorPagination'
import { parseApiError } from '../app/utils/api-error'
import * as adjudication from '../app/features/admin/adjudication-preview'

const source = await Bun.file(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdSubmissionsPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')
const factory = (deps: Record<string, unknown>) => new Function('deps',
  `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAdminCompetitionsByIdSubmissionsPage;`)(deps)
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

type Query = {
  competitionChallengeId: string | null; teamId: string | null; gameplayFactKind: string | null
  state: string | null; gameplayFactResult: string | null; value: string | null
  offset: number; limit: number; desc: boolean
}

async function harness() {
  const facts = Array.from({ length: 23 }, (_, index) => ({
    id: `fact-${index + 1}`, competitionChallengeId: index < 12 ? 'challenge-a' : 'challenge-b',
    teamId: 'team-a', kind: 'FlagAttempt', state: 'Completed', result: 'Wrong', value: `flag{${index + 1}}`,
  }))
  const requests: Query[] = []
  const writes: unknown[] = []
  let failNextRead = false
  let mount!: () => void
  let unmount!: () => void
  const scope = effectScope()
  const state = scope.run(() => factory({
    ...adjudication, ref, computed, watch, proxyRefs, useOffsetPagination, useCursorPagination, parseApiError, Download: {},
    useCompetitionAdmin: () => ({ competitionId: 'competition', role: ref('Owner'), canWrite: ref(true), canJudge: ref(true) }),
    useAuth: () => ({ isAdministrator: ref(true) }),
    adminUserPath: (id: string) => `/admin/users/${id}`,
    adminTeamPath: () => '/team', adminChallengePath: () => '/challenge',
    onMounted: (callback: () => void) => { mount = callback },
    onUnmounted: (callback: () => void) => { unmount = callback },
    adminListCompetitionChallenges: async () => ({ data: { items: [{ id: 'challenge-a', title: 'A' }, { id: 'challenge-b', title: 'B' }] } }),
    adminListTeams: async () => ({ data: { items: [{ id: 'team-a', name: 'Team A' }] } }),
    adminListGameplayFacts: async ({ path, query }: { path: { competitionId: string }; query: Query }) => {
      expect(path.competitionId).toBe('competition')
      requests.push({ ...query })
      if (failNextRead) {
        failNextRead = false
        return { error: { status: 503, detail: '列表暂时不可用' } }
      }
      const filtered = facts.filter(fact => (!query.competitionChallengeId || fact.competitionChallengeId === query.competitionChallengeId)
        && (!query.teamId || fact.teamId === query.teamId) && (!query.gameplayFactKind || fact.kind === query.gameplayFactKind)
        && (!query.state || fact.state === query.state) && (!query.gameplayFactResult || fact.result === query.gameplayFactResult)
        && (!query.value || fact.value.includes(query.value)))
      return { data: { items: filtered.slice(query.offset, query.offset + query.limit), total: filtered.length } }
    },
    adminCreateGameplayFactRejudgement: async (request: unknown) => { writes.push(request); return { data: {} } },
    translate: (key: string) => key, describeMessage: (key: string) => ({ key }),
    toast: { success: () => {}, error: (error: unknown) => { throw error } },
  })())!
  mount()
  await drain()
  return { state, facts, requests, writes, failNextRead: () => { failNextRead = true }, stop: () => { unmount(); scope.stop() } }
}

describe('competition submission pagination', () => {
  test('requests bounded numbered pages with the total and replaces rows when paging or changing page size', async () => {
    const app = await harness()
    try {
      const { state } = app
      expect(state.pageLimit.value).toBe(10)
      expect(state.total.value).toBe(23)
      expect(state.pageCount.value).toBe(3)
      expect(state.items.value.map((fact: { id: string }) => fact.id)).toEqual(app.facts.slice(0, 10).map(fact => fact.id))
      expect(app.requests[0]).toMatchObject({ offset: 0, limit: 10, desc: true })
      await state.loadPage(2)
      expect(app.requests.at(-1)).toMatchObject({ offset: 10, limit: 10, desc: true })
      expect(state.items.value.map((fact: { id: string }) => fact.id)).toEqual(app.facts.slice(10, 20).map(fact => fact.id))
      await state.loadPage(3)
      expect(state.items.value).toHaveLength(3)
      await state.setPageSize(20)
      expect(state.page.value).toBe(1)
      expect(state.pageCount.value).toBe(2)
      expect(state.items.value).toHaveLength(20)
      expect(app.requests.at(-1)).toMatchObject({ offset: 0, limit: 20, desc: true })
      await state.loadPage(2)
      expect(state.items.value).toHaveLength(3)
    }
    finally { app.stop() }
  })

  test('pages the applied filters, resets on apply and clear, and handles zero matches', async () => {
    const app = await harness()
    try {
      const { state } = app
      await state.loadPage(3)
      state.filterChallenge.value = 'challenge-a'
      state.filterTeam.value = 'team-a'
      state.filterKind.value = 'FlagAttempt'
      state.filterState.value = 'Completed'
      state.filterResult.value = 'Wrong'
      state.filterFlag.value = 'flag{'
      await state.applyFilters()
      expect(state.page.value).toBe(1)
      expect(state.total.value).toBe(12)
      expect(app.requests.at(-1)).toEqual({ competitionChallengeId: 'challenge-a', teamId: 'team-a',
        gameplayFactKind: 'FlagAttempt', state: 'Completed', gameplayFactResult: 'Wrong', value: 'flag{', offset: 0, limit: 10, desc: true })
      state.filterFlag.value = 'missing'
      await state.loadPage(2)
      expect(state.items.value).toHaveLength(2)
      expect(app.requests.at(-1)?.value).toBe('flag{')
      await state.applyFilters()
      expect(state.page.value).toBe(1)
      expect(state.total.value).toBe(0)
      expect(state.pageCount.value).toBe(1)
      expect(state.items.value).toEqual([])
      state.onClickFilterChallenge()
      await drain()
      expect(state.total.value).toBe(23)
      expect(state.page.value).toBe(1)
      expect(app.requests.at(-1)).toEqual({ competitionChallengeId: null, teamId: null,
        gameplayFactKind: null, state: null, gameplayFactResult: null, value: null, offset: 0, limit: 10, desc: true })
    }
    finally { app.stop() }
  })

  test('reloads a bounded first page after single and batch rejudgement, retaining the selected page size', async () => {
    const app = await harness()
    try {
      const { state } = app
      await state.setPageSize(20)
      await state.loadPage(2)
      await state.rejudgeOne('fact-21')
      await drain()
      expect(app.writes[0]).toEqual({ path: { competitionId: 'competition' }, body: { targetKind: 'GameplayFact', targetId: 'fact-21' } })
      expect(state.page.value).toBe(1)
      expect(state.pageLimit.value).toBe(20)
      expect(state.items.value).toHaveLength(20)
      await state.loadPage(2)
      state.batchTarget.value = 'challenge-a'
      await state.rejudgeBatch()
      await drain()
      expect(app.writes[1]).toEqual({ path: { competitionId: 'competition' }, body: { targetKind: 'CompetitionChallenge', targetId: 'challenge-a' } })
      expect(state.page.value).toBe(1)
      expect(state.items.value).toHaveLength(20)
      expect(app.requests.at(-1)).toMatchObject({ offset: 0, limit: 20 })
    }
    finally { app.stop() }
  })

  test('keeps loaded rows beside a page failure and can retry the requested page', async () => {
    const app = await harness()
    try {
      const { state } = app
      const firstPage = state.items.value.map((fact: { id: string }) => fact.id)
      app.failNextRead()
      await state.loadPage(2)
      expect(state.listError.value.message).toBe('列表暂时不可用')
      expect(state.items.value.map((fact: { id: string }) => fact.id)).toEqual(firstPage)
      expect(state.loading.value).toBeFalse()
      await state.loadPage(2)
      expect(state.listError.value).toBeNull()
      expect(state.items.value.map((fact: { id: string }) => fact.id)).toEqual(app.facts.slice(10, 20).map(fact => fact.id))
    }
    finally { app.stop() }
  })
})
