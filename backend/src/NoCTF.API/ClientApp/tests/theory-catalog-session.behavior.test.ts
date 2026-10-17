import { expect, test } from 'bun:test'
import { computed, effectScope, ref, watch } from 'vue'
import { featureController } from './support/feature-controller'

type Catalog = ReturnType<typeof import('../app/features/theory/useTheoryActivityCatalogPage').useTheoryActivityCatalogPage>
type SessionResult = { data?: { canAnswer: boolean; canIssue: boolean }; error?: unknown }
const activity = { id: 'activity', name: 'Practice' }
const settle = () => new Promise(resolve => setTimeout(resolve, 0))

async function fixture(read: (id: string) => Promise<SessionResult>, staff = false) {
  const unmount: Array<() => void> = [], navigations: unknown[] = []
  const make = await featureController<Catalog>('features/theory/useTheoryActivityCatalogPage.ts', 'useTheoryActivityCatalogPage', {
    computed, ref, watch,
    useRoute: () => ({ path: staff ? '/admin/theory/activities' : '/theory' }),
    useRouter: () => ({ push: (target: unknown) => { navigations.push(target) } }),
    useAuth: () => ({ canOrganize: ref(staff) }),
    listTheoryActivities: async () => ({ data: { items: [activity], total: 1 } }),
    getTheorySession: ({ path }: { path: { activityId: string } }) => read(path.activityId),
    onMounted: () => {}, onBeforeUnmount: (callback: () => void) => unmount.push(callback),
    message: (key: string) => ({ key }),
    parseApiError: (_cause: unknown, fallback: unknown) => ({ displayMessage: fallback }),
    activityStateLabel: () => '', scoringLabel: () => '',
  })
  const scope = effectScope(), state = scope.run(make)!
  return { state, navigations, dispose: () => { unmount.forEach(callback => callback()); scope.stop() } }
}

test('SDK session errors expose a recoverable row error and retain activity navigation', async () => {
  const f = await fixture(async () => ({ error: { status: 503 } }))
  try {
    await f.state.load(); await settle()
    expect(f.state.rows.value).toEqual([activity])
    expect(f.state.sessions.value.activity).toBeUndefined()
    expect(f.state.sessionErrors.value.activity).toEqual({ key: 'theory.loadFailed' })
    f.state.open(activity)
    expect(f.navigations).toEqual([{ path: '/theory/activity', query: {} }])
  }
  finally { f.dispose() }
})

test('retry restores an active paper without replacing the activity list', async () => {
  let reads = 0
  const f = await fixture(async () => ++reads === 1
    ? { error: { status: 503 } } : { data: { canAnswer: true, canIssue: false } })
  try {
    await f.state.load(); await settle()
    const rows = f.state.rows.value
    await f.state.retrySession('activity')
    expect(f.state.sessionErrors.value.activity).toBeUndefined()
    expect(f.state.sessions.value.activity?.canAnswer).toBe(true)
    expect(f.state.rows.value).toBe(rows)
    expect(reads).toBe(2)
  }
  finally { f.dispose() }
})

test('a failed transport promise is handled and can be retried', async () => {
  const f = await fixture(async () => { throw new Error('Network unavailable') })
  try {
    await f.state.load(); await settle()
    expect(f.state.sessionErrors.value.activity).toBeDefined()
    expect(f.state.sessions.value.activity).toBeUndefined()
  }
  finally { f.dispose() }
})

test('pending lookups do not fabricate a completed session or duplicate requests', async () => {
  let finish!: (result: SessionResult) => void, requests = 0
  const f = await fixture(async () => { requests++; return new Promise(resolve => { finish = resolve }) })
  try {
    await f.state.load()
    await f.state.retrySession('activity')
    expect(f.state.sessions.value.activity).toBeUndefined()
    expect(f.state.sessionErrors.value.activity).toBeUndefined()
    expect(requests).toBe(1)
    finish({ data: { canAnswer: false, canIssue: true } }); await settle()
    expect(f.state.sessions.value.activity?.canIssue).toBe(true)
    f.state.open(activity, true)
    expect(f.navigations).toEqual([{ path: '/theory/activity', query: { new: '1' } }])
  }
  finally { f.dispose() }
})

test('older page lookups cannot overwrite a newer page session with an error', async () => {
  const pending: Array<(result: SessionResult) => void> = []
  const f = await fixture(async () => new Promise(resolve => pending.push(resolve)))
  try {
    await f.state.load(); await f.state.load()
    pending[1]!({ data: { canAnswer: true, canIssue: false } }); await settle()
    pending[0]!({ error: { status: 503 } }); await settle()
    expect(f.state.sessions.value.activity?.canAnswer).toBe(true)
    expect(f.state.sessionErrors.value.activity).toBeUndefined()
  }
  finally { f.dispose() }
})

test('unmounted catalogs ignore pending lookups', async () => {
  let finish!: (result: SessionResult) => void
  const f = await fixture(async () => new Promise(resolve => { finish = resolve }))
  await f.state.load(); f.dispose()
  finish({ data: { canAnswer: true, canIssue: false } }); await settle()
  expect(f.state.sessions.value).toEqual({})
})

test('staff catalogs do not probe participant sessions', async () => {
  let requests = 0
  const f = await fixture(async () => { requests++; return { data: { canAnswer: false, canIssue: false } } }, true)
  try {
    await f.state.load(); await settle()
    expect(requests).toBe(0)
    f.state.open(activity)
    expect(f.navigations).toEqual(['/admin/theory/activities/activity'])
  }
  finally { f.dispose() }
})
