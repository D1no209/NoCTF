import { expect, test } from 'bun:test'
import { computed, effectScope, markRaw, reactive, ref, watch } from 'vue'
import { testAdminResource } from './support/admin-competition-resource'
import { competitionChallengeManagementPath, isCompetitionChallengeManagementRoute } from '../app/features/admin/admin-navigation'
import * as soloNavigation from '../app/features/live-solo/navigation'

async function compile(path: string, name: string, dependencies: Record<string, unknown>, middleware = false) {
  const source = await Bun.file(new URL(`../app/${path}`, import.meta.url)).text()
  let code = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  if (middleware) code = code.replace('export default ', 'return ')
  else code = code.replace(/export function /g, 'function ') + `; return ${name};`
  for (const match of source.matchAll(/import (\w+) from '[^']+\.vue'/g)) dependencies[match[1]!] = {}
  for (const icon of 'Activity ChartNoAxesCombined ClipboardCheck Container Download FileCheck GitBranch KeyRound LayoutDashboard Mail Network Orbit Puzzle Settings ShieldAlert Trophy Users Webhook'.split(' ')) dependencies[icon] = {}
  return new Function('deps', `const { ${Object.keys(dependencies).join(', ')} } = deps; ${code}`)(dependencies)
}

test('direct configuration entry shares the guard response with the shell and editor, including later subpage entry', async () => {
  let reads = 0, context: any, gameplayContext: any, fail = false
  const resource = testAdminResource(async () => {
    reads++
    if (fail) return { error: {}, response: new Response(null, { status: 503 }) }
    return { data: { competition: { id: 'contest', mode: 'Ctf', ownerId: 'actor', administrationRole: 'Owner' },
      modeConfiguration: { competitionId: 'contest', mode: 'Ctf' } } }
  })
  const mounted: Array<() => unknown> = []
  const session = { user: ref({ userId: 'actor' }), isLoggedIn: ref(true), isAdministrator: ref(false) }
  const route = reactive({ path: '/admin/competitions/contest/configuration', params: { id: 'contest' }, query: {}, fullPath: '/admin/competitions/contest/configuration' })
  const deps: Record<string, unknown> = {
    ...soloNavigation, computed, ref, watch, markRaw, competitionChallengeManagementPath, isCompetitionChallengeManagementRoute,
    useAuth: () => session, useAdminCompetitionResource: () => resource,
    useRoute: () => route, defineNuxtRouteMiddleware: (callback: unknown) => callback,
    translate: (key: string) => key, describeMessage: (key: string) => key,
    navigateTo: () => { throw new Error('Unexpected redirect') },
    parseApiError: () => ({ displayMessage: 'failed' }),
    createError: (value: object) => Object.assign(new Error('Navigation failed'), value),
    competitionPath: (id: string) => `/competitions/${id}`, adminWorkspacePath: (path: string) => path,
    CompetitionAdminKey: Symbol(), competitionContextKey: Symbol(),
    provide: (_key: unknown, value: any) => { if ('canWrite' in value) context = value; if ('standing' in value) gameplayContext = value },
    useCompetitionAdmin: () => context,
    onMounted: (callback: () => unknown) => mounted.push(callback), onUnmounted: () => {},
    watchCompetition: () => () => {}, isoToLocalInput: (value: unknown) => value ?? '',
    normalizeConfigValue: (value: unknown) => JSON.stringify(value ?? null),
    shouldHydrateMetaField: (previous: Record<string, unknown> | null, current: Record<string, unknown>, key: string, force = false) =>
      force || previous === null || current[key] === previous[key],
  }
  const guard = await compile('middleware/competition-admin.ts', '', { ...deps }, true)
  await guard(route, { path: '/competitions' })
  const scope = effectScope()
  try {
    const shell = await compile('features/routes/admin/competitions/useAdminCompetitionsByIdPage.ts', 'useAdminCompetitionsByIdPage', { ...deps })
    scope.run(() => shell())
    const editor = await compile('features/routes/admin/competitions/[id]/useAdminCompetitionsByIdConfigurationPage.ts', 'useAdminCompetitionsByIdConfigurationPage', { ...deps })
    const page = scope.run(() => editor())!
    for (const callback of mounted) await callback()
    expect(page.config.value.mode).toBe('Ctf')
    expect(context.canWrite.value).toBe(true)
    expect(reads).toBe(1)
    await guard({ ...route, path: '/admin/competitions/contest/teams' }, route)
    await context.read()
    expect(reads).toBe(1)
    fail = true
    await context.refresh()
    expect(context.competition.value.id).toBe('contest')
    expect(context.canWrite.value).toBe(false)
    expect(context.canJudge.value).toBe(false)
    expect(gameplayContext.managementFresh.value).toBe(false)
    expect(gameplayContext.competition.value.administrationRole).toBe('Observer')
  } finally { scope.stop() }
})
