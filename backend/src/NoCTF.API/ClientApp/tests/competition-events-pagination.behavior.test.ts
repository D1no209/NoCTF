import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { competitionEventHistoryRange } from '../app/lib/competition-event-history'
import { createTrailingRefresh } from '../app/lib/latest-page-refresh'
import { competitionChallengesPath } from '../app/utils/app-routes'
import { adminCompetitionPath } from '../app/features/admin/admin-navigation'
import { parseApiError } from '../app/utils/api-error'

const source = await Bun.file(new URL('../app/features/routes/competitions/[id]/useCompetitionsByIdEventsPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')
const factory = (deps: Record<string, unknown>) => new Function('deps',
  `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useCompetitionsByIdEventsPage;`)(deps)
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

type Query = { offset: number; limit: number; desc: boolean; kind: string | null; from?: string; to?: string }
type Options = { administrator?: boolean; organizer?: boolean; staffRole?: string; ready?: boolean; initialKind?: string }

async function harness(options: Options = {}) {
  const events = Array.from({ length: 23 }, (_, index) => ({
    id: `event-${index + 1}`, kind: index < 12 ? 'AnnouncementPublished' : 'ChallengePublished',
    level: 'Information', occurredAt: new Date(Date.now() - index * 60_000).toISOString(),
  }))
  const competition = ref<{ startTime: string } | null>(options.ready === false ? null : { startTime: '2024-01-01T00:00:00Z' })
  const requests: Query[] = []
  let scopeRequests = 0
  let failNextRead = false
  let nextReadGate: Promise<void> | null = null
  let mount!: () => void
  let unmount!: () => void
  let handlers!: { competitionEventChanged: (event: { occurredAt: string }) => void; onReconnected: () => void }
  let unsubscribed = false
  const scope = effectScope()
  const state = scope.run(() => factory({
    ref, computed, watch, useOffsetPagination, competitionEventHistoryRange, createTrailingRefresh, competitionChallengesPath, adminCompetitionPath, parseApiError,
    ArrowLeft: {}, competitionContextKey: {}, inject: () => ({ competition }),
    useRoute: () => ({ params: { id: 'competition' }, query: { kind: options.initialKind } }),
    useAuth: () => ({ isAdministrator: ref(options.administrator ?? true), canOrganize: ref(options.organizer ?? false) }),
    onMounted: (callback: () => void) => { mount = callback },
    onUnmounted: (callback: () => void) => { unmount = callback },
    watchCompetition: (id: string, callbacks: typeof handlers) => {
      expect(id).toBe('competition')
      handlers = callbacks
      return () => { unsubscribed = true }
    },
    adminGetCompetition: async () => {
      scopeRequests += 1
      return { data: { competition: { administrationRole: options.staffRole ?? null } } }
    },
    listCompetitionEvents: async ({ path, query }: { path: { competitionId: string }; query: Query }) => {
      expect(path.competitionId).toBe('competition')
      requests.push({ ...query })
      const gate = nextReadGate
      nextReadGate = null
      if (gate) await gate
      if (failNextRead) {
        failNextRead = false
        return { error: { status: 503, detail: '动态暂时不可用' } }
      }
      const filtered = events.filter(event => !query.kind || query.kind === event.kind)
      return { data: { items: filtered.slice(query.offset, query.offset + query.limit), total: filtered.length } }
    },
    translate: (key: string) => key, describeMessage: (key: string) => ({ key }),
  })())!
  mount()
  await state.initialize()
  return {
    state, events, competition, requests, scopeRequests: () => scopeRequests,
    emit: (occurredAt = new Date().toISOString()) => handlers.competitionEventChanged({ occurredAt }),
    reconnect: () => handlers.onReconnected(),
    failNextRead: () => { failNextRead = true },
    deferNextRead: () => {
      let release!: () => void
      nextReadGate = new Promise<void>(resolve => { release = resolve })
      return release
    },
    stop: () => { unmount(); scope.stop(); expect(unsubscribed).toBeTrue() },
  }
}

describe('competition event pagination and navigation', () => {
  test('requests numbered pages, replaces rows and resets when changing the page size', async () => {
    const app = await harness()
    try {
      const { state } = app
      expect(state.competitionReturnPath.value).toBe('/admin/competitions/competition')
      expect(state.competitionReturnLabel.value).toBe('competitions.label.backToAdministration')
      expect(state.pageLimit.value).toBe(10)
      expect(state.pageCount.value).toBe(3)
      expect(state.total.value).toBe(23)
      expect(state.items.value).toEqual(app.events.slice(0, 10))
      expect(app.requests[0]).toMatchObject({ offset: 0, limit: 10, desc: true, kind: null })
      expect(app.requests[0]).not.toHaveProperty('cursor')
      await state.loadPage(2)
      expect(state.items.value).toEqual(app.events.slice(10, 20))
      expect(app.requests.at(-1)).toMatchObject({ offset: 10, limit: 10 })
      await state.loadPage(3)
      expect(state.items.value).toHaveLength(3)
      await state.setPageSize(20)
      expect(state.page.value).toBe(1)
      expect(state.pageCount.value).toBe(2)
      expect(state.items.value).toHaveLength(20)
      expect(app.requests.at(-1)).toMatchObject({ offset: 0, limit: 20 })
    }
    finally { app.stop() }
  })

  test('applies the initial kind and resets to page one on every filter change, including empty results', async () => {
    const app = await harness({ initialKind: 'AnnouncementPublished' })
    try {
      const { state } = app
      expect(state.total.value).toBe(12)
      expect(app.requests[0]?.kind).toBe('AnnouncementPublished')
      await state.loadPage(2)
      expect(state.items.value).toHaveLength(2)
      state.kind.value = 'ChallengePublished'
      await drain()
      expect(state.page.value).toBe(1)
      expect(state.total.value).toBe(11)
      expect(app.requests.at(-1)).toMatchObject({ offset: 0, kind: 'ChallengePublished' })
      state.kind.value = 'FirstBloodAwarded'
      await drain()
      expect(state.total.value).toBe(0)
      expect(state.pageCount.value).toBe(1)
      expect(state.items.value).toEqual([])
      state.kind.value = 'all'
      await drain()
      expect(state.total.value).toBe(23)
      expect(app.requests.at(-1)?.kind).toBeNull()
    }
    finally { app.stop() }
  })

  test('retains the current page for manual and live refreshes and coalesces notifications during a refresh', async () => {
    const app = await harness()
    try {
      const { state } = app
      await state.setPageSize(20)
      await state.loadPage(2)
      const before = app.requests.length
      const release = app.deferNextRead()
      const refresh = state.reload()
      app.emit()
      app.emit()
      release()
      await refresh
      expect(app.requests).toHaveLength(before + 2)
      expect(app.requests.slice(before).every(query => query.offset === 20 && query.limit === 20)).toBeTrue()
      expect(state.page.value).toBe(2)
      expect(state.items.value).toEqual(app.events.slice(20))
      app.reconnect()
      await drain()
      expect(state.page.value).toBe(2)
      expect(app.requests.at(-1)).toMatchObject({ offset: 20, limit: 20 })
    }
    finally { app.stop() }
  })

  test('preserves staff access and participant date boundaries, covering server timestamps ahead of the browser', async () => {
    const staff = await harness({ administrator: false, organizer: true, staffRole: 'Judge' })
    try {
      expect(staff.scopeRequests()).toBe(1)
      expect(staff.state.hasStaffHistory.value).toBeTrue()
      expect(staff.state.competitionReturnPath.value).toBe('/competitions/competition/challenges')
      expect(staff.state.competitionReturnLabel.value).toBe('common.label.backCompetition')
      expect(staff.requests[0]?.from).toBeUndefined()
      expect(staff.requests[0]?.to).toBeUndefined()
    }
    finally { staff.stop() }
    const participant = await harness({ administrator: false })
    try {
      expect(participant.scopeRequests()).toBe(0)
      expect(participant.state.hasStaffHistory.value).toBeFalse()
      expect(participant.state.competitionReturnPath.value).toBe('/competitions/competition/challenges')
      expect(participant.state.competitionReturnLabel.value).toBe('common.label.backCompetition')
      const initial = participant.requests[0]!
      expect(Date.parse(initial.to!) - Date.parse(initial.from!)).toBe(30 * 24 * 60 * 60 * 1000)
      const serverTime = new Date(Date.now() + 60_000).toISOString()
      participant.emit(serverTime)
      await drain()
      expect(participant.requests.at(-1)?.to).toBe(serverTime)
    }
    finally { participant.stop() }
  })

  test('waits for parent competition readiness and preserves loaded rows when a refresh fails', async () => {
    const app = await harness({ ready: false })
    try {
      const { state } = app
      expect(app.requests).toHaveLength(0)
      expect(state.initialized.value).toBeFalse()
      app.competition.value = { startTime: '2024-01-01T00:00:00Z' }
      await drain()
      expect(state.initialized.value).toBeTrue()
      expect(state.items.value).toHaveLength(10)
      await state.loadPage(2)
      app.failNextRead()
      await state.reload()
      expect(state.error.value.message).toBe('动态暂时不可用')
      expect(state.items.value).toEqual(app.events.slice(10, 20))
      expect(state.loading.value).toBeFalse()
      await state.reload()
      expect(state.error.value).toBeNull()
      expect(state.page.value).toBe(2)
    }
    finally { app.stop() }
  })
})
