import { expect, test } from 'bun:test'
import { computed, effectScope, markRaw, ref, watch } from 'vue'
import * as navigation from '../app/features/live-solo/navigation'
import { adminWorkspacePath } from '../app/features/admin/admin-navigation'
import { canManageLiveSolo } from '../app/features/live-solo/settings-draft'
import { deploymentCandidates, poolDraft, poolDraftErrors } from '../app/features/live-solo/pool-draft'

test('management and player navigation remain in their own workspace and preserve the selected match', () => {
  for (const management of [false, true]) {
    const base = management ? navigation.liveSoloManagementPath('contest') : navigation.liveSoloPlayerPath('contest')
    const match = navigation.liveSoloMatchPath('contest', 'series', base)
    expect(navigation.liveSoloBasePath('contest', match)).toBe(base)
    expect(navigation.liveSoloMatchListLocation('contest', match, 'series')).toEqual({ path: base, query: { match: 'series' } })
    expect(navigation.isLiveSoloManagementPath(match)).toBe(management)
  }
  expect(navigation.isLiveSoloManagementPath('/competitions/contest/live-solo/recordings/series?staff=1')).toBe(false)
  expect(navigation.isLiveSoloManagementPath('/admin/competitions/contest/live-solo-other')).toBe(false)
})

test('changing the active question does not replace the administration match frame or interrupt its screen state', () => {
  const match = '/admin/competitions/contest/live-solo/matches/series'
  expect(adminWorkspacePath(`${match}/rounds/round/questions/first`)).toBe(match)
  expect(adminWorkspacePath(`${match}/rounds/round/questions/second`)).toBe(match)
  expect(adminWorkspacePath('/admin/competitions/contest/live-solo/groups/list')).toBe('/admin/competitions/contest/live-solo/groups/list')
})

const source = await Bun.file(new URL('../app/features/live-solo/useLiveSoloQuestionPool.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
function poolFixture(group?: string) {
  const calls: unknown[] = [], scope = effectScope()
  let leaveGuard!: (to: { fullPath: string }) => boolean
  const dependencies = { ...navigation, computed, ref, watch, markRaw, poolDraft, poolDraftErrors, deploymentCandidates, canManageLiveSolo,
    useRoute: () => ({ path: '/admin/competitions/contest/live-solo/question-pool', params: { id: 'contest' }, query: { group } }),
    useRouter: () => ({ push: async (target: unknown) => { calls.push(target) } }),
    useAuth: () => ({ user: ref({ userId: 'owner' }), canOrganize: ref(true) }),
    inject: () => ({ competition: ref({ mode: 'LiveSolo', administrationRole: 'Owner' }), refresh: async () => {} }),
    competitionContextKey: Symbol(), DefinitionEditor: {},
    useOffsetPagination: () => ({ items: ref([]), page: ref(1), pageCount: ref(1), total: ref(0), limit: ref(20), loading: ref(false), reset: () => {} }),
    listLiveSoloQuestionPool: async () => ({ data: { items: [{ id: 'question', title: 'Synthetic question', direction: 'Web', ownership: 'CompetitionOwned', concurrencyStamp: 'stamp', materialStamp: 'material', flags: [], attachments: [] }] } }),
    exportLiveSoloPoolQuestion: async () => ({ data: { challengeId: 'bank-copy' } }),
    onBeforeRouteLeave: (guard: typeof leaveGuard) => { leaveGuard = guard }, onMounted: () => {}, onScopeDispose: () => {},
    message: (key: string) => ({ key }), parseLiveSoloError: () => ({ displayMessage: 'failure' }),
  }
  const factory = new Function('deps', `const { ${Object.keys(dependencies).join(',')} } = deps; ${compiled}; return useLiveSoloQuestionPool;`)(dependencies)
  const state = scope.run(() => factory())!
  return { state, calls, leave: (path: string) => leaveGuard({ fullPath: path }), stop: () => scope.stop() }
}

test('saving a bank copy keeps the current competition draft and offers an explicit link instead of forcing a workspace change', async () => {
  const f = poolFixture()
  try {
    await f.state.load(); await f.state.exportToBank()
    expect(f.calls).toEqual([])
    expect(f.state.current.value.id).toBe('question')
    expect(f.state.exportedTemplatePath.value).toBe('/admin/challenges/bank-copy')
  } finally { f.stop() }
})

test('question-pool handoff returns to the originating management question list with the selected instance', async () => {
  const f = poolFixture('list')
  try {
    await f.state.load(); await f.state.back()
    expect(f.calls).toEqual([{ path: '/admin/competitions/contest/live-solo/groups/list', query: { added: 'question' } }])
  } finally { f.stop() }
})

test('sidebar navigation and switching to the player view cannot silently discard unsaved pool content', async () => {
  const f = poolFixture()
  try {
    await f.state.load(); f.state.draft.value.title = 'Unsaved local content'
    expect(f.leave('/competitions/contest/live-solo')).toBe(false)
    expect(f.state.leaveOpen.value).toBe(true)
    expect(f.state.draft.value.title).toBe('Unsaved local content')
    expect(f.calls).toEqual([])
    await f.state.discard()
    expect(f.calls).toEqual(['/competitions/contest/live-solo'])
  } finally { f.stop() }
})
