import { testAdminResource } from './support/admin-competition-resource'
import * as soloNavigation from '../app/features/live-solo/navigation'
import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, proxyRefs, reactive, ref, unref, watch } from 'vue'
import { readdir } from 'node:fs/promises'
import { resolveCompetitionBrowser } from '../app/features/competitions/competition-browser'
import { competitionPath, competitionsPath } from '../app/utils/app-routes'
import { competitionChallengeManagementPath, isCompetitionChallengeManagementRoute } from '../app/features/admin/admin-navigation'

const source = async (path: string) => Bun.file(new URL(`../app/${path}`, import.meta.url)).text()
async function controller(path: string, name: string, dependencies: Record<string, unknown>, props?: object) {
  if (dependencies.useCompetitionAdmin && dependencies.adminGetCompetition) {
    const getContext = dependencies.useCompetitionAdmin as () => object
    dependencies.useCompetitionAdmin = () => ({ ...getContext(), read: dependencies.adminGetCompetition })
  }
  const input = await source(path)
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(input)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
  const deps: Record<string, unknown> = { ...soloNavigation, computed, ref, watch, markRaw, reactive, proxyRefs, competitionChallengeManagementPath,
    translate: (key: string) => key, describeMessage: (key: string) => key,
    onMounted: () => {}, onUnmounted: () => {}, watchCompetition: () => () => {}, navigateTo: () => {}, ...dependencies }
  deps.useAdminCompetitionResource = (id: string) => testAdminResource(dependencies.adminGetCompetition as any, id)
  for (const match of input.matchAll(/import (\w+) from '[^']+\.vue'/g)) deps[match[1]!] = {}
  for (const icon of ['Plus', 'ArrowRight', 'Box', 'CalendarRange', 'Clock', 'EyeOff', 'FileText', 'KeyRound', 'LogIn', 'Settings', 'ShieldCheck', 'Trophy', 'UserPlus', 'Users',
    'Activity', 'ChartNoAxesCombined', 'ClipboardCheck', 'Container', 'Download', 'FileCheck', 'GitBranch', 'LayoutDashboard', 'Mail', 'Network', 'Orbit', 'Puzzle', 'ShieldAlert', 'Webhook']) deps[icon] = {}
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return ${name};`)(deps)
  const scope = effectScope(), state = scope.run(() => factory(props ? reactive(props) : undefined, () => {}))!
  return { state, stop: () => scope.stop() }
}
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const auth = (role = false, signedIn = true) => ({ isAdministrator: ref(role), isLoggedIn: ref(signedIn), user: ref<any>(signedIn ? { userId: 'staff' } : null) })

async function middleware(session: ReturnType<typeof auth>, response: any) {
  const code = new Bun.Transpiler({ loader: 'ts' }).transformSync(await source('middleware/competition-admin.ts'))
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace('export default ', 'return ')
  const calls: string[] = []
  const deps = { ...soloNavigation, useAuth: () => session, defineNuxtRouteMiddleware: (fn: unknown) => fn,
    navigateTo: (target: unknown) => target, competitionPath, competitionChallengeManagementPath, isCompetitionChallengeManagementRoute, translate: (key: string) => key,
    createError: (error: object) => Object.assign(new Error('request failed'), error),
    useAdminCompetitionResource: (id: string) => testAdminResource(async () => { calls.push(id); return response }, id) }
  const guard = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${code}`)(deps)
  return { guard: (id = 'contest', section = 'submissions', ccId?: string) => {
    const path = `/admin/competitions/${id}/${section}${ccId ? '/' + ccId : ''}`
    return guard({ params: { id, ccId }, path, fullPath: `${path}?page=2`, query: { page: '2' } })
  }, calls }
}

describe('competition staff access', () => {
  test.each(['Owner', 'Manager', 'Judge', 'Observer'])('%s has a single Solo question-pool entry and can find its question lists', async role => {
    const mounted: Array<() => Promise<void>> = []
    const app = await controller('features/routes/admin/competitions/useAdminCompetitionsByIdPage.ts', 'useAdminCompetitionsByIdPage', {
      useAuth: () => auth(), useRoute: () => ({ params: { id: 'contest' }, path: '/admin/competitions/contest' }),
      adminGetCompetition: async () => ({ data: { competition: { id: 'contest', mode: 'LiveSolo', ownerId: 'owner', administrationRole: role } } }),
      provide: () => {}, competitionContextKey: Symbol(), CompetitionAdminKey: Symbol(), adminWorkspacePath: (path: string) => path, gameModeLabel: (mode: string) => mode,
      onMounted: (fn: () => Promise<void>) => mounted.push(fn),
    })
    try {
      await mounted[0]!()
      const links = app.state.navGroups.value.flatMap((group: any) => group.items).map((item: any) => item.to)
      expect(links.filter((path: string) => path === '/admin/competitions/contest/live-solo/question-pool')).toHaveLength(1)
      expect(links).toContain('/admin/competitions/contest/live-solo/groups')
      expect(links).not.toContain('/admin/competitions/contest/challenges')
    } finally { app.stop() }
  })

  test.each(['Ctf', 'Awd', 'Awdp', 'Koh', 'LiveSolo'])('%s exposes and invokes bulk Flag generation only where supported', async mode => {
    let calls = 0
    const app = await controller('features/routes/admin/competitions/[id]/useAdminCompetitionsByIdIndexPage.ts', 'useAdminCompetitionsByIdIndexPage', {
      useAuth: () => auth(true), useCompetitionAdmin: () => ({ competitionId: 'contest', competition: ref({ mode, status: 'Published' }), canWrite: ref(true), canManagePermissions: ref(true), refresh: async () => {} }),
      useCompetitionPoster: () => ({ posterUrl: ref(null), posterLoading: ref(false), posterError: ref(null), refreshPoster: async () => {}, clearPoster: async () => {}, cancelPoster: () => {} }),
      adminGenerateMissingFlags: async () => { calls++; return { data: { failures: [] } } },
      adminPreviewCompetitionHardDelete: async () => ({ data: { canHardDelete: false, references: [] } }),
      parseApiError: () => ({ displayMessage: 'failed' }),
      toast: { success: () => {}, warning: () => {}, error: () => {} }, startGateErrorMessage: () => '',
    })
    try {
      const supported = mode === 'Ctf' || mode === 'Koh'
      expect(unref(app.state.canGenerateMissingFlags)).toBe(supported)
      await app.state.generateMissingFlags()
      expect(calls).toBe(supported ? 1 : 0)
    } finally { app.stop() }
  })

  test.each(['Administrator', 'Owner', 'Manager', 'Judge', 'Observer'])('%s enters the Solo pool instead of mounting ordinary publication controls', async role => {
    const app = await middleware(auth(role === 'Administrator'), { data: { competition: { mode: 'LiveSolo', administrationRole: role } } })
    expect(await app.guard('contest', 'challenges')).toEqual({ path: '/admin/competitions/contest/live-solo/question-pool', query: { page: '2' } })
    expect(await app.guard('contest', 'challenges', 'selected-question')).toEqual({ path: '/admin/competitions/contest/live-solo/question-pool', query: { page: '2', question: 'selected-question' } })
    expect(app.calls).toEqual(['contest', 'contest'])
  })

  test('ordinary challenge management keeps its own route and publication workflow', async () => {
    for (const mode of ['Ctf', 'Awd', 'Awdp', 'Koh']) {
      const app = await middleware(auth(true), { data: { competition: { mode } } })
      expect(await app.guard('contest', 'challenges')).toBeUndefined()
      expect(competitionChallengeManagementPath('contest', mode as 'Ctf')).toBe('/admin/competitions/contest/challenges')
    }
  })

  test('Solo administration routes resolve the actual resource mode even for platform administrators', async () => {
    const ordinary = await middleware(auth(true), { data: { competition: { mode: 'Ctf' } } })
    expect(await ordinary.guard('contest', 'live-solo/settings')).toBe('/admin/competitions/contest')
    expect(ordinary.calls).toEqual(['contest'])
    const observer = await middleware(auth(), { data: { competition: { mode: 'LiveSolo', administrationRole: 'Observer' } } })
    expect(await observer.guard('contest', 'live-solo')).toBeUndefined()
    const outsider = await middleware(auth(), { error: {}, response: { status: 403 } })
    expect(await outsider.guard('contest', 'live-solo')).toBe('/competitions/contest')
  })

  test.each(['Owner', 'Manager', 'Judge', 'Observer'] as const)('%s sees the management entry and enters its own authorized resource', async role => {
    const session = auth()
    const overview = await controller('features/competitions/useCompetitionOverview.ts', 'useCompetitionOverview', {
      useAuth: () => session,
      useCompetitionPoster: () => ({ posterUrl: ref(null), cancelPoster: () => {}, refreshPoster: async () => {} }),
    }, { competition: { id: 'contest', status: 'Draft', administrationRole: role } })
    try {
      expect(overview.state.canManageCompetition.value).toBe(true)
      expect(overview.state.managementOnly.value).toBe(true)
      const app = await middleware(session, { data: { competition: { administrationRole: role } } })
      expect(await app.guard()).toBeUndefined()
      expect(app.calls).toEqual(['contest'])
    } finally { overview.stop() }
  })

  test('a user without competition permissions has no entry; knowing the route does not grant access', async () => {
    const session = auth()
    const overview = await controller('features/competitions/useCompetitionOverview.ts', 'useCompetitionOverview', {
      useAuth: () => session,
      useCompetitionPoster: () => ({ posterUrl: ref(null), cancelPoster: () => {}, refreshPoster: async () => {} }),
    }, { competition: { id: 'contest', status: 'Running', administrationRole: null } })
    try {
      expect(overview.state.canManageCompetition.value).toBe(false)
      const app = await middleware(session, { error: {}, response: { status: 404 } })
      expect(await app.guard('other')).toBe('/competitions/other')
      expect(app.calls).toEqual(['other'])
    } finally { overview.stop() }
  })

  test('anonymous deep links preserve the complete destination; platform administrators retain access', async () => {
    const guest = await middleware(auth(false, false), {})
    expect(await guest.guard()).toEqual({ path: '/auth/login', query: { redirect: '/admin/competitions/contest/submissions?page=2' } })
    expect(guest.calls).toEqual([])
    const platform = await middleware(auth(true), { data: { competition: { id: 'contest', mode: 'Ctf' } } })
    expect(await platform.guard()).toBeUndefined()
    expect(platform.calls).toEqual(['contest'])
  })

  test('the guard checks each target and does not turn a dependency failure into permission', async () => {
    const result: any = { data: { competition: { administrationRole: 'Manager' } } }
    const app = await middleware(auth(), result)
    expect(await app.guard()).toBeUndefined()
    result.data = undefined; result.error = {}; result.response = { status: 403 }
    expect(await app.guard()).toBe('/competitions/contest')
    result.response.status = 503
    await expect(app.guard()).rejects.toMatchObject({ statusCode: 503 })
    expect(app.calls).toHaveLength(3)
  })

  test('resource guards cover every competition administration route while platform guards remain separate', async () => {
    const directory = new URL('../app/pages/admin/competitions/', import.meta.url)
    const routes = await readdir(directory, { recursive: true })
    for (const route of routes.filter(path => path.endsWith('.vue'))) {
      const page = await Bun.file(new URL(route.replaceAll('\\', '/'), directory)).text()
      expect(page).toContain("middleware: 'competition-admin'")
      expect(page).not.toContain("middleware: 'platform-admin'")
    }
    expect(await source('pages/admin/platform.vue')).toContain("middleware: 'platform-admin'")
    expect(await source('middleware/platform-admin.ts')).toContain('!isAdministrator.value')
  })

  test.each(['Owner', 'Manager', 'Judge', 'Observer'] as const)('%s receives only its existing write, adjudication and permission capabilities', async role => {
    const mounted: Array<() => Promise<void>> = []
    let context: any
    let response: any = { data: { competition: { id: 'contest', mode: 'Ctf', ownerId: 'owner', administrationRole: role } } }
    const app = await controller('features/routes/admin/competitions/useAdminCompetitionsByIdPage.ts', 'useAdminCompetitionsByIdPage', {
      useAuth: () => auth(), useRoute: () => ({ params: { id: 'contest' }, path: '/admin/competitions/contest' }),
      adminGetCompetition: async () => response, provide: (_key: unknown, value: unknown) => { if (value && typeof value === 'object' && 'canWrite' in value) context = value }, competitionContextKey: Symbol(), CompetitionAdminKey: Symbol(),
      adminWorkspacePath: (path: string) => path, gameModeLabel: (mode: string) => mode,
      parseApiError: () => ({ displayMessage: 'failed' }), onMounted: (fn: () => Promise<void>) => mounted.push(fn),
    })
    try {
      await mounted[0]!()
      expect(app.state.role.value).toBe(role)
      expect(context.canWrite.value).toBe(role === 'Owner' || role === 'Manager')
      expect(context.canJudge.value).toBe(role !== 'Observer')
      expect(context.canManagePermissions.value).toBe(role === 'Owner')
      const links = app.state.navGroups.value.flatMap((group: any) => group.items).map((item: any) => item.to)
      for (const suffix of ['configuration', 'tracks', 'directions', 'exports', 'webhooks', 'progression', 'leaderboard'])
        expect(links.includes(`/admin/competitions/contest/${suffix}`)).toBe(role === 'Owner' || role === 'Manager')
      for (const suffix of ['announcements', 'cheats', 'traffic-captures'])
        expect(links.includes(`/admin/competitions/contest/${suffix}`)).toBe(role !== 'Observer')
      for (const suffix of ['challenges', 'teams', 'submissions', 'runtimes', 'writeups'])
        expect(links).toContain(`/admin/competitions/contest/${suffix}`)
      const writeups = app.state.navGroups.value.flatMap((group: any) => group.items).find((item: any) => item.to.endsWith('/writeups'))
      expect(writeups.label).toBe(role === 'Observer' ? 'administration.navigation.viewWriteups' : 'writeUp.review')
      const permissionsLink = app.state.navGroups.value.flatMap((group: any) => group.items).some((item: any) => item.to.endsWith('/permissions'))
      expect(permissionsLink).toBe(role === 'Owner')
      response = { error: {}, response: { status: 404 } }
      await app.state.refresh()
      expect(app.state.competition.value).toBeNull()
      expect(app.state.role.value).toBe('Observer')
      expect(context.canWrite.value).toBe(false)
      expect(context.canJudge.value).toBe(false)
    } finally { app.stop() }
  })
})

describe('staff competition discovery', () => {
  const publicContest = { id: 'public', status: 'Running' as const }
  const managedDraft = { id: 'draft', status: 'Draft' as const, administrationRole: 'Manager' as const }
  async function browser(session: ReturnType<typeof auth>, list: () => Promise<any>) {
    const calls: any[] = []
    const app = await controller('features/routes/competitions/useCompetitionsIndexPage.ts', 'useCompetitionsIndexPage', {
      useAuth: () => session, useRoute: () => ({ params: { id: 'draft' }, query: {} }), useRouter: () => ({ push: () => {}, replace: () => {} }),
      resolveCompetitionBrowser, competitionPath, competitionsPath,
      listCompetitionsEndpoint: async () => ({ data: { items: [publicContest] } }),
      adminListCompetitions: async (request: any) => { calls.push(request); return list() }, parseApiError: () => ({ displayMessage: 'failed' }),
    })
    return { ...app, calls }
  }
  test('staff discover their draft and public competitions without duplication or deleted entries', async () => {
    const app = await browser(auth(), async () => ({ data: { items: [managedDraft,
      { ...publicContest, administrationRole: 'Judge' }, { ...managedDraft, id: 'deleted', deletedAt: 'now' }] } }))
    try {
      await drain()
      expect(app.calls[0].query.includeDeleted).toBe(false)
      expect(app.state.selected.value).toMatchObject(managedDraft)
      expect(app.state.counts.value).toMatchObject({ running: 1, upcoming: 1, deleted: 0 })
      expect(resolveCompetitionBrowser([managedDraft], 'draft', null).selected?.id).toBe('draft')
      expect(resolveCompetitionBrowser([{ ...managedDraft, administrationRole: null }], 'draft', null).selected).toBeNull()
    } finally { app.stop() }
  })
  test('an account change clears staff entries and discards the old account response', async () => {
    const session = auth()
    let finish!: (value: any) => void
    let requests = 0
    const app = await browser(session, () => ++requests === 1 ? new Promise(resolve => { finish = resolve }) : Promise.resolve({ data: { items: [] } }))
    try {
      session.user.value = { userId: 'other' }
      await drain()
      finish({ data: { items: [managedDraft] } })
      await drain()
      expect(app.state.selected.value).toBeNull()
      expect(app.state.missing.value).toBe(true)
      session.isLoggedIn.value = false; session.user.value = null
      await drain()
      expect(app.calls).toHaveLength(2)
    } finally { app.stop() }
  })
})
