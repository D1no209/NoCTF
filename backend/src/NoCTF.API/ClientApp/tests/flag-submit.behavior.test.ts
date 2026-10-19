import { expect, test } from 'bun:test'
import { computed, effectScope, nextTick, reactive, ref, toRefs, watch } from 'vue'
import { parseApiError } from '../app/utils/api-error'
import { message as describeMessage, translate } from '../app/utils/i18n'

const source = await Bun.file(new URL('../app/features/challenges/useFlagSubmit.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source).replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
function harness() {
  const submissions: unknown[] = [], judgements: unknown[] = []
  let poll = async () => true, dispose = () => {}
  const deps = { computed, ref, watch, toRefs, describeMessage, translate, parseApiError, PartyPopper: {},
    useAuth: () => ({ user: ref({ userId: 'player' }) }), useHumanVerification: () => ({ request: async () => ({}) }),
    inject: () => async () => ({ state: 'Completed', result: 'Correct' }), challengeGameplayFactStatusKey: Symbol(),
    createCommandAttempt: () => ({ headers: () => ({}), completed() {} }),
    useSubmissionTiming: () => ({ submissionsClosed: ref(false), judgementOnly: ref(false) }),
    usePolling: (callback: typeof poll) => { poll = callback; return { timedOut: ref(false), error: ref(null), start() {}, stop() {} } },
    submitFlagEndpoint: async (request: unknown) => { submissions.push(request); return { data: { gameplayFactId: `fact-${submissions.length}` } } },
    judgeAwdpBreakFlag: async (request: unknown) => { judgements.push(request); return { data: { result: 'Correct' } } },
    isGameplayFactPending: (state: string) => state === 'Pending',
    requestAnimationFrame: (fn: () => void) => fn(), setTimeout: () => 0, clearTimeout() {},
    onMounted() {}, onUnmounted: (fn: () => void) => { dispose = fn },
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useFlagSubmit;`)(deps)
  function mount(overrides: Record<string, unknown> = {}) {
    const props = reactive({ competitionId: 'competition', competitionChallengeId: 'challenge', completion: 'Solved',
      multiple: false, practice: false, readOnlyJudgement: false, initiallySolved: false, ...overrides })
    const scope = effectScope(), state = scope.run(() => factory(props, () => {}))
    return { props, state, stop() { dispose(); scope.stop() } }
  }
  return { mount, submissions, judgements, poll: () => poll() }
}

test('AWD accepts distinct flags after success and after remount without a solved cache lock', async () => {
  const app = harness(), first = app.mount({ completion: 'Repeatable', multiple: true })
  first.state.input.value = 'flag{first}'; await first.state.submit(); await app.poll()
  expect(first.state.inputDisabled.value).toBe(false)
  expect(first.state.solved.value).toBe(false)
  first.state.input.value = 'flag{second}'; await first.state.submit(); await app.poll()
  expect(app.submissions).toHaveLength(2)
  first.stop()
  const remounted = app.mount({ completion: 'Repeatable', multiple: true })
  try {
    expect(remounted.state.inputDisabled.value).toBe(false)
    remounted.state.input.value = 'flag{third}'; await remounted.state.submit()
    expect(app.submissions).toHaveLength(3)
    remounted.props.remainingAttempts = 0; await nextTick()
    // Remaining attempts are advisory; the Worker returns the authoritative asynchronous result.
    expect(remounted.state.inputDisabled.value).toBe(false)
  } finally { remounted.stop() }
})

test('CTF restored solved state remains editable without replaying a success notification', async () => {
  const app = harness(), mounted = app.mount({ initiallySolved: true })
  try {
    expect(mounted.state.solved.value).toBe(true)
    expect(mounted.state.inputDisabled.value).toBe(false)
    expect(mounted.state.persistentResult.value).toBeNull()
    mounted.state.input.value = 'flag{check}'; await mounted.state.submit()
    expect(app.submissions).toHaveLength(1)
  } finally { mounted.stop() }
})

test('AWD preserves flag bytes while treating CRLF only as the batch line boundary', async () => {
  const app = harness(), mounted = app.mount({ completion: 'Repeatable', multiple: true })
  try {
    mounted.state.input.value = ' flag{first} \r\nflag{second}\t\r\n'
    await mounted.state.submit()
    expect(app.submissions[0]).toMatchObject({ body: { flags: [' flag{first} ', 'flag{second}\t'] } })
  } finally { mounted.stop() }
})

test('AWDP locks an achieved Break until the panel switches to read-only judgement', async () => {
  const app = harness(), mounted = app.mount({ completion: 'Break' })
  try {
    mounted.state.input.value = 'flag{break}'; await mounted.state.submit(); await app.poll()
    expect(mounted.state.inputDisabled.value).toBe(true)
    mounted.state.input.value = 'flag{again}'; await mounted.state.submit()
    expect(app.submissions).toHaveLength(1)
    mounted.props.readOnlyJudgement = true; await nextTick()
    expect(mounted.state.inputDisabled.value).toBe(false)
    await mounted.state.submit()
    expect(app.judgements).toHaveLength(1)
    expect(app.submissions).toHaveLength(1)
  } finally { mounted.stop() }
})
