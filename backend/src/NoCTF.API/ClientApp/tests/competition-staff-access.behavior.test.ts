import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, reactive, ref, watch } from 'vue'
import { readdir } from 'node:fs/promises'
import { resolveCompetitionBrowser } from '../app/features/competitions/competition-browser'
import { competitionPath, competitionsPath } from '../app/utils/app-routes'

const source = async (path: string) => Bun.file(new URL(`../app/${path}`, import.meta.url)).text()
async function controller(path: string, name: string, dependencies: Record<string, unknown>, props?: object) {
  const input = await source(path)
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(input)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
  const deps: Record<string, unknown> = { computed, ref, watch, markRaw, reactive,
    translate: (key: string) => key, describeMessage: (key: string) => key,
    onMounted: () => {}, onUnmounted: () => {}, ...dependencies }
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
  const deps = { useAuth: () => session, defineNuxtRouteMiddleware: (fn: unknown) => fn,
    navigateTo: (target: unknown) => target, competitionPath, translate: (key: string) => key,
    createError: (error: object) => Object.assign(new Error('request failed'), error),
    adminGetCompetition: async ({ path }: any) => { calls.push(path.competitionId); return response } }
  const guard = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${code}`)(deps)
  return { guard: (id = 'contest') => guard({ params: { id }, fullPath: `/admin/competitions/${id}/submissions?page=2` }), calls }
}

describe('competition staff access', () => {
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
    const platform = await middleware(auth(true), {})
    expect(await platform.guard()).toBeUndefined()
    expect(platform.calls).toEqual([])
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
      adminGetCompetition: async () => response, provide: (_key: unknown, value: unknown) => { context = value }, CompetitionAdminKey: Symbol(),
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
      for (const suffix of ['configuration', 'tracks', 'directions', 'announcements', 'exports', 'webhooks', 'progression', 'leaderboard'])
        expect(links.includes(`/admin/competitions/contest/${suffix}`)).toBe(role === 'Owner' || role === 'Manager')
      for (const suffix of ['cheats', 'traffic-captures'])
        expect(links.includes(`/admin/competitions/contest/${suffix}`)).toBe(role !== 'Observer')
      for (const suffix of ['challenges', 'teams', 'submissions', 'runtimes', 'writeups'])
        expect(links).toContain(`/admin/competitions/contest/${suffix}`)
      const writeups = app.state.navGroups.value.flatMap((group: any) => group.items).find((item: any) => item.to.endsWith('/writeups'))
      expect(writeups.label).toBe(role === 'Observer' ? 'administration.navigation.viewWriteups' : 'writeUp.review')
      const permissionsLink = app.state.navGroups.value.flatMap((group: any) => group.items).some((item: any) => item.to.endsWith('/permissions'))
      expect(permissionsLink).toBe(role === 'Owner')
      response = { error: {}, response: { status: 404 } }
      await mounted[0]!()
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
