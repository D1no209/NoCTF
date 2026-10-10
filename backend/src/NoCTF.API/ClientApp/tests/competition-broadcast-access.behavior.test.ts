import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, reactive, ref, toRefs, watch } from 'vue'
import { createTrailingRefresh } from '../app/lib/latest-page-refresh'
import { adminCompetitionPath, competitionPath, isCompetitionOverviewPath } from '../app/utils/app-routes'
import { competitionBroadcastIdentity, competitionBroadcastQueryWindow, competitionBroadcastKinds, isCompetitionBroadcastKind, mergeCompetitionBroadcasts, previousCompetitionBroadcastWindow } from '../app/utils/competition-broadcast'

async function factory(path: string, name: string, additional: Record<string, unknown>) {
  const input = await Bun.file(new URL(`../app/${path}`, import.meta.url)).text()
  const code = new Bun.Transpiler({ loader: 'ts' }).transformSync(input)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export /g, '')
  const deps: Record<string, unknown> = { ref, computed, watch, markRaw, toRefs, createTrailingRefresh,
    translate: (key: string) => key, describeMessage: (key: string) => key, parseApiError: () => ({ displayMessage: 'failed' }), ...additional }
  for (const match of input.matchAll(/import (\w+) from '[^']+\.vue'/g)) deps[match[1]!] = {}
  for (const icon of ['ArrowLeft', 'ClipboardCheck', 'FileText', 'GitBranch', 'LayoutDashboard', 'MessageCircleQuestion', 'Puzzle', 'Trophy', 'UserRound', 'Megaphone']) deps[icon] = {}
  return new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${code}; return ${name};`)(deps)
}
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const competition = { id: 'competition', mode: 'Ctf' as const, status: 'Running' as const, startTime: '2026-01-01T00:00:00Z' }

async function shell() {
  const user = ref<any>({ userId: 'actor' })
  let context: any, team: any = null, role: any = null
  let competitionReader: (() => Promise<any>) | undefined
  let events: any
  const mounted: Array<() => void> = []
  const contextKey = Symbol(), navigationKey = Symbol()
  const build = await factory('features/routes/competitions/useCompetitionsByIdPage.ts', 'useCompetitionsByIdPage', {
    useAuth: () => ({ user, isAdministrator: ref(false) }),
    useRoute: () => ({ path: '/competitions/competition/my/team', params: { id: 'competition' } }), useRouter: () => ({ replace: () => {} }),
    useCompetitionAnnouncementCatchUp: () => ({ refreshMissedAnnouncements: async () => {} }),
    getCompetitionEndpoint: async () => competitionReader ? competitionReader() : ({ data: { ...competition, administrationRole: role } }),
    getMyTeamEndpoint: async () => team ? { data: team } : { response: { status: 404 } },
    getLeaderboardEndpoint: async () => ({ data: { teams: [] } }), getPlayerCompetitionProgression: async () => ({ data: { enabled: false } }),
    provide: (key: symbol, value: unknown) => { if (key === contextKey) context = value }, competitionContextKey: contextKey,
    competitionWorkspaceNavigationKey: navigationKey, competitionPath, adminCompetitionPath, isCompetitionOverviewPath,
    onMounted: (fn: () => void) => mounted.push(fn), onUnmounted: () => {},
    watchCompetition: (_id: string, handlers: unknown) => { events = handlers; return () => {} },
  })
  const scope = effectScope(), state = scope.run(build)!
  await state.initialize()
  mounted.forEach(fn => fn())
  return { state, context, user, scope, setTeam: (value: unknown) => { team = value }, setRole: (value: unknown) => { role = value },
    setCompetitionReader: (reader: () => Promise<any>) => { competitionReader = reader },
    notify: (kind: string) => events.competitionEventChanged({ kind }), stop: () => scope.stop() }
}

async function panel(allowed: boolean, fetcher: (request: any) => Promise<any> = async () => ({ data: { items: [{ id: 'event', kind: 'FirstBloodAwarded', occurredAt: '2026-01-01T00:00:01Z' }] } })) {
  const canReadBroadcasts = ref(allowed), requests: any[] = [], mounted: Array<() => void> = [], unmounted: Array<() => void> = []
  let subscriptions = 0, unsubscribe = 0, handlers: any
  const build = await factory('features/competition/useCompetitionBroadcastPanel.ts', 'useCompetitionBroadcastPanel', {
    inject: () => ({ competition: ref(competition), canReadBroadcasts }), competitionContextKey: Symbol(),
    competitionBroadcastIdentity, competitionBroadcastQueryWindow, competitionBroadcastKinds, isCompetitionBroadcastKind, mergeCompetitionBroadcasts, previousCompetitionBroadcastWindow,
    motionAttributes: () => ({}), listCompetitionEvents: (request: any) => { requests.push(request); return fetcher(request) },
    onMounted: (fn: () => void) => mounted.push(fn), onUnmounted: (fn: () => void) => unmounted.push(fn),
    watchCompetition: (_id: string, callbacks: unknown) => { subscriptions++; handlers = callbacks; return () => { unsubscribe++ } },
  })
  const scope = effectScope(), state = scope.run(() => build(reactive({ competitionId: competition.id, fill: true })))!
  mounted.forEach(fn => fn())
  return { state, canReadBroadcasts, requests, subscriptions: () => subscriptions, unsubscribe: () => unsubscribe,
    notify: () => handlers?.competitionEventChanged({ kind: 'FirstBloodAwarded', occurredAt: '2026-01-01T00:00:02Z' }),
    stop: () => { unmounted.forEach(fn => fn()); scope.stop() } }
}

describe('broadcast access follows competition eligibility', () => {
  test('no team, unregistered, pending, rejected and banned teams cannot see broadcasts', async () => {
    const app = await shell()
    try {
      expect(app.context.canReadBroadcasts.value).toBe(false)
      for (const registrationStatus of ['Unregistered', 'Pending', 'Rejected']) {
        app.setTeam({ id: 'team', registrationStatus, isBanned: false })
        await app.state.refreshMyTeam()
        expect(app.context.canReadBroadcasts.value).toBe(false)
      }
      app.setTeam({ id: 'team', registrationStatus: 'Approved', isBanned: true })
      await app.state.refreshMyTeam()
      expect(app.context.canReadBroadcasts.value).toBe(false)
      app.setTeam({ id: 'team', registrationStatus: 'Approved', isBanned: false })
      await app.state.refreshMyTeam()
      expect(app.context.canReadBroadcasts.value).toBe(true)
    } finally { app.stop() }
  })

  test.each(['Owner', 'Manager', 'Judge', 'Observer'] as const)('%s can view broadcasts without a participant registration', async role => {
    const app = await shell()
    try {
      app.setRole(role)
      await app.context.refresh()
      expect(app.context.canReadBroadcasts.value).toBe(true)
      app.user.value = null
      expect(app.context.canReadBroadcasts.value).toBe(false)
    } finally { app.stop() }
  })

  test('switching accounts cannot inherit the previous staff or team qualification', async () => {
    const app = await shell()
    try {
      app.setRole('Manager'); await app.context.refresh()
      expect(app.context.canReadBroadcasts.value).toBe(true)
      app.setRole(null); app.setTeam(null)
      app.user.value = { userId: 'other' }
      expect(app.context.canReadBroadcasts.value).toBe(false)
      await drain()
      expect(app.context.canReadBroadcasts.value).toBe(false)
    } finally { app.stop() }
  })

  test('registration, membership and staff changes refresh broadcast eligibility', async () => {
    const app = await shell()
    try {
      app.setTeam({ id: 'team', registrationStatus: 'Approved', isBanned: false })
      app.notify('TeamRegistrationChanged'); await drain()
      expect(app.context.canReadBroadcasts.value).toBe(true)
      app.setTeam(null); app.notify('TeamMemberRemoved'); await drain()
      expect(app.context.canReadBroadcasts.value).toBe(false)
      app.setRole('Observer'); app.notify('CompetitionUpdated'); await drain()
      expect(app.context.canReadBroadcasts.value).toBe(true)
      app.setRole(null); app.notify('CompetitionUpdated'); await drain()
      expect(app.context.canReadBroadcasts.value).toBe(false)
    } finally { app.stop() }
  })

  test('an older competition read cannot restore a revoked staff role', async () => {
    const app = await shell()
    let finish!: (value: any) => void
    try {
      app.setCompetitionReader(() => new Promise(resolve => { finish = resolve }))
      const stale = app.context.refresh()
      app.setCompetitionReader(async () => ({ data: { ...competition, administrationRole: null } }))
      await app.context.refresh()
      finish({ data: { ...competition, administrationRole: 'Manager' } })
      await stale
      expect(app.context.canReadBroadcasts.value).toBe(false)
      expect(app.state.competition.value.administrationRole).toBeNull()
    } finally { app.stop() }
  })

  test('an ineligible panel makes no requests or event subscriptions', async () => {
    const app = await panel(false)
    try {
      await app.state.refreshLatest()
      expect(app.requests).toHaveLength(0)
      expect(app.subscriptions()).toBe(0)
      expect(app.state.items.value).toEqual([])
      expect(app.state.error.value).toBeNull()
    } finally { app.stop() }
  })

  test('qualification enables broadcasts; revocation clears existing rows and subscriptions', async () => {
    const app = await panel(false)
    try {
      app.canReadBroadcasts.value = true
      await drain()
      expect(app.requests).toHaveLength(1)
      expect(app.subscriptions()).toBe(1)
      expect(app.state.items.value).toHaveLength(1)
      app.canReadBroadcasts.value = false
      await drain()
      expect(app.unsubscribe()).toBe(1)
      expect(app.state.items.value).toEqual([])
      app.notify(); await app.state.refreshLatest()
      expect(app.requests).toHaveLength(1)
    } finally { app.stop() }
  })

  test('a late response after permission loss is aborted and cannot repopulate broadcasts', async () => {
    let finish!: (value: any) => void
    const app = await panel(true, async () => new Promise(resolve => { finish = resolve }))
    try {
      app.canReadBroadcasts.value = false
      await drain()
      expect(app.requests[0].signal.aborted).toBe(true)
      finish({ data: { items: [{ id: 'old', kind: 'FirstBloodAwarded' }] } })
      await app.state.refreshLatest()
      expect(app.state.items.value).toEqual([])
      expect(app.state.error.value).toBeNull()
      expect(app.requests).toHaveLength(1)
    } finally { app.stop() }
  })
})
