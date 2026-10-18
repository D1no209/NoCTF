import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, reactive, ref, toRefs, watch } from 'vue'

type Request = { path: { competitionId: string, competitionChallengeId: string }, signal: AbortSignal }
type Reply = { data?: { id: string }, error?: object }
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

async function mountDetail(read: (request: Request) => Promise<Reply>, progressionEnabled = false) {
  const input = await Bun.file(new URL('../app/features/challenges/useCompetitionChallengeDetail.ts', import.meta.url)).text()
  const code = new Bun.Transpiler({ loader: 'ts' }).transformSync(input)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
  const calls: Array<{ method: string, target: string }> = []
  const unmount: Array<() => void> = []
  const competition = ref({ mode: 'Ctf', status: 'Running', progressionEnabled })
  const props = reactive({ competitionId: 'contest', competitionChallengeId: 'first' })
  const dependencies: Record<string, unknown> = {
    computed, markRaw, reactive, ref, toRefs, watch,
    inject: () => ({ competition, standing: ref(null) }), provide: () => {},
    competitionContextKey: Symbol(), challengeGameplayFactStatusKey: Symbol(),
    createChallengeGameplayFactStatusReader: () => () => {},
    onBeforeUnmount: (callback: () => void) => unmount.push(callback),
    useAuth: () => ({ isLoggedIn: ref(true), ready: ref(true), user: ref({ userId: 'player' }) }),
    useRoute: () => ({ query: {} }), useRouter: () => ({ push: () => {} }),
    adminFormatDateTime: (value: unknown) => value, describeMessage: (key: string) => key,
    parseApiError: () => ({ displayMessage: 'detail unavailable' }),
    getChallengeEndpoint: (request: Request) => {
      calls.push({ method: 'GET', target: request.path.competitionChallengeId })
      return read(request)
    },
    listChallengeAttachmentsEndpoint: async () => ({ data: { items: [] } }),
    // These traps detect regressions in the actual controller execution, including the old preflight.
    startProgressionChallenge: async () => { calls.push({ method: 'POST', target: 'start' }); return {} },
    useState: () => ref(null),
    navigateTo: () => { calls.push({ method: 'NAVIGATE', target: 'progression' }) },
    ensureLocaleDomains: async () => {},
  }
  for (const match of input.matchAll(/import (\w+) from '[^']+\.vue'/g)) dependencies[match[1]!] = {}
  for (const icon of ['Dice5', 'FileDown', 'History', 'BookOpen']) dependencies[icon] = {}
  const factory = new Function('deps', `const { ${Object.keys(dependencies).join(', ')} } = deps; ${code}; return useCompetitionChallengeDetail;`)(dependencies)
  const scope = effectScope()
  const state = scope.run(() => factory(props))!
  return { state, props, competition, calls, stop: () => { unmount.forEach(callback => callback()); scope.stop() } }
}

describe('challenge detail reads', () => {
  test.each([
    ['ordinary CTF', false],
    ['enabled progression', true],
    ['disabled progression with saved nodes', false],
  ] as const)('%s loads directly without a start preflight', async (_name, enabled) => {
    const app = await mountDetail(async request => ({ data: { id: request.path.competitionChallengeId } }), enabled)
    try {
      await drain()
      expect(app.calls).toEqual([{ method: 'GET', target: 'first' }])
      expect(app.state.challenge.value.id).toBe('first')
      expect(app.state.loading.value).toBe(false)
    } finally { app.stop() }
  })

  test('a denied read displays the detail error without redirecting to the map', async () => {
    const app = await mountDetail(async () => ({ error: { status: 404 } }), true)
    try {
      await drain()
      expect(app.calls).toEqual([{ method: 'GET', target: 'first' }])
      expect(app.state.challenge.value).toBeNull()
      expect(app.state.error.value).toBe('detail unavailable')
      expect(app.state.loading.value).toBe(false)
    } finally { app.stop() }
  })

  test('switching challenges ignores a late response for the previous challenge', async () => {
    let finishFirst!: (reply: Reply) => void
    const app = await mountDetail(request => request.path.competitionChallengeId === 'first'
      ? new Promise(resolve => { finishFirst = resolve })
      : Promise.resolve({ data: { id: 'second' } }))
    try {
      app.props.competitionChallengeId = 'second'
      await drain()
      finishFirst({ data: { id: 'first' } })
      await drain()
      expect(app.state.challenge.value.id).toBe('second')
      expect(app.calls).toEqual([{ method: 'GET', target: 'first' }, { method: 'GET', target: 'second' }])
    } finally { app.stop() }
  })

  test('unmount aborts the read and prevents a late response from publishing', async () => {
    let finish!: (reply: Reply) => void
    let signal!: AbortSignal
    const app = await mountDetail(request => {
      signal = request.signal
      return new Promise(resolve => { finish = resolve })
    })
    app.stop()
    expect(signal.aborted).toBe(true)
    finish({ data: { id: 'first' } })
    await drain()
    expect(app.state.challenge.value).toBeNull()
  })
})
