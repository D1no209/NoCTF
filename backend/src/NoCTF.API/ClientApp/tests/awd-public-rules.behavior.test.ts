import { expect, test } from 'bun:test'
import { computed, effectScope, nextTick, reactive, ref, watch } from 'vue'
import { publicPolicyValues, publicRuleGroups, publicRuleSource, publicFlagWindow } from '../app/features/challenges/awd/awd-public-rules'
import { awdScoringPolicy, awdScoringValues } from '../app/features/admin/awd/awd-scoring-policy'
import { message, translate } from '../app/utils/i18n'
import { parseApiError } from '../app/utils/api-error'
import { createMockApi } from '../mock/api'
import { createMockAwdPublicRules } from '../mock/awd-public-rules'
import { createFixtures, mockModeConfiguration } from '../mock/data/fixtures'
import { id } from '../mock/schema'
import type { NoCtfapiEndpointsChallengesAwdPublicRulesResponse as Rules, NoCtfapiEndpointsChallengesAwdPublicRulePolicyResponse as Policy } from '../app/api'
import type { useAwdPublicRules } from '../app/features/challenges/awd/useAwdPublicRules'

const scoring = awdScoringPolicy(awdScoringValues({}))
const policy: Policy = { rulesId: 'issued', attackMode: 'FixedPerAttack', attackPoints: 50, defensePoints: 100, slaPoints: 100,
  checkerIntervalSeconds: 30, options: { preset: 'Custom', flagLifetimeUnit: 'Rounds', flagLifetime: 5,
    historicalLifetimeUnit: 'ActiveSeconds', historicalLifetime: 200, linkHistoricalWindow: true }, scoring, stores: [] }
const rules: Rules = { roundNumber: 4, issuedPolicies: [policy], nextRound: policy, attackPointsOverridden: true,
  questionOverrides: { recoveringRatio: 0, retryCount: 0, linkHistoricalWindow: false },
  ownService: { enabled: true, health: null, execution: 'Pending', settlement: 'Provisional', lastTrustedHealth: 'Ok' } }

test('public values use effective server scoring without changing original issuance or inventing health', () => {
  const changed = { ...policy, scoring: { ...scoring, attackPoints: 75 }, scoringChangeEventId: 'amendment' }
  expect(publicPolicyValues(changed).attackPoints).toBe(75)
  expect(publicPolicyValues(changed, false).attackPoints).toBe(50)
  const row = publicRuleGroups(rules, changed, false).flatMap(group => group.rows).find(row => row.key === 'attackPoints')!
  expect(row.value).toBe('75'); expect(row.issued).toBe('50')
  expect(row.source).toBe(translate('challenges.awdRules.source.amendment'))
  expect(publicRuleGroups(rules, changed, false).flatMap(group => group.rows).some(row => row.key === 'flagTemplate')).toBe(false)
})

test('zero and false overrides keep their source while preset constraints and linked windows are explicit', () => {
  for (const field of ['attackPoints', 'recoveringRatio', 'retryCount', 'linkHistoricalWindow'])
    expect(publicRuleSource(rules, field, true)).toBe(translate('challenges.awdRules.source.question'))
  expect(publicRuleSource(rules, 'slaWeight', true)).toBe(translate('challenges.awdRules.source.competition'))
  expect(publicRuleSource({ ...rules, nextRound: { ...policy, options: { ...policy.options, preset: 'SharedPool' } } }, 'attackRewardMode', true))
    .toBe(translate('challenges.awdRules.source.preset'))
  expect(publicFlagWindow(policy, true)).toBe(publicFlagWindow(policy, false))
  expect(publicFlagWindow({ ...policy, options: { ...policy.options, linkHistoricalWindow: false } }, true))
    .toBe(translate('challenges.awdRules.secondsWindow', { count: 200 }))
})

const source = await Bun.file(new URL('../app/features/challenges/awd/useAwdPublicRules.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source).replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await Promise.resolve(); await Promise.resolve() }
async function harness() {
  const requests: { path: { competitionId: string; competitionChallengeId: string }; signal: AbortSignal; resolve: (value: unknown) => void }[] = []
  const props = reactive({ competitionId: 'competition', competitionChallengeId: 'first' })
  let unmount = () => {}
  const deps = { computed, ref, watch, publicRuleGroups, publicFlagWindow, message, translate, parseApiError,
    getAwdRules: (input: object) => new Promise(resolve => requests.push({ ...input, resolve } as typeof requests[number])),
    ensureLocaleDomains: async () => {}, watchCompetition: () => () => {}, onMounted: () => {},
    onBeforeUnmount: (fn: () => void) => { unmount = fn }, clearInterval: () => {}, document: { removeEventListener() {} } }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAwdPublicRules;`)(deps)
  const scope = effectScope(), state = scope.run(() => factory(props)) as ReturnType<typeof useAwdPublicRules>
  await drain()
  return { props, state, requests, stop() { unmount(); scope.stop() } }
}

test('question switches reject old responses and never inherit a previous trusted health as current', async () => {
  const app = await harness()
  try {
    app.props.competitionChallengeId = 'second'; await drain()
    expect(app.requests[0]!.signal.aborted).toBe(true)
    app.requests[0]!.resolve({ data: { ...rules, roundNumber: 999 } }); await drain()
    expect(app.state.rules.value).toBeNull()
    app.requests[1]!.resolve({ data: rules }); await drain()
    expect(app.state.rules.value?.roundNumber).toBe(4)
    expect(app.state.serviceText.value).toBe(translate('challenges.awdRules.health.Pending'))
    expect(app.state.selectedRulesId.value).toBe('issued')
  } finally { app.stop() }
})

test('refresh coalesces, preserves the last snapshot on transient errors, and clears personalized data on revoked access', async () => {
  const app = await harness()
  try {
    app.requests[0]!.resolve({ data: rules }); await drain()
    const refresh = app.state.loadRules(); await drain(); await app.state.loadRules()
    expect(app.requests).toHaveLength(2)
    app.requests[1]!.resolve({ error: { status: 503 }, response: { status: 503 } }); await refresh; await drain()
    expect(app.state.rules.value?.ownService).toEqual(rules.ownService)
    expect(app.state.error.value).not.toBeNull(); expect(app.requests).toHaveLength(3)
    app.requests[2]!.resolve({ error: { status: 403 }, response: { status: 403 } }); await drain()
    expect(app.state.rules.value).toBeNull()
  } finally { app.stop() }
})

test('local issued policies stay fixed while edited defaults change only the next policy', () => {
  const state = createFixtures(), competition = state.competitions.find(item => item.mode === 'Awd')!, challenge = state.challenges.find(item => item.competitionId === competition.id)!
  const read = createMockAwdPublicRules(), config = mockModeConfiguration('Awd') as { awd: { attackPoints: number } }
  const first = read(competition, challenge, undefined, state.settings, undefined)
  config.awd.attackPoints = 123; state.settings.set(`mode-configuration:${competition.id}`, config)
  const next = read(competition, challenge, undefined, state.settings, undefined)
  expect(next.issuedPolicies?.[0]?.attackPoints).toBe(first.issuedPolicies?.[0]?.attackPoints)
  expect(next.nextRound?.attackPoints).toBe(123); expect(next.ownService).toBeNull()
})

test('generated public route gives anonymous readers no team data and authenticated readers only safe current health', async () => {
  const api = createMockApi(), base = 'http://127.0.0.1:5081', path = `/api/v1/competitions/${id(2, 2)}/challenges/${id(4, 101)}/awd/rules`
  const anonymous = await (await api.handle(new Request(base + path))).json()
  expect(anonymous.ownService).toBeNull(); expect(anonymous.nextRound.attackPoints).toBe(50)
  const login = await (await api.handle(new Request(base + '/api/v1/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'player', password: 'Mock123!' }) }))).json()
  const own = await (await api.handle(new Request(base + path, { headers: { Authorization: `Bearer ${login.accessToken}` } }))).json()
  expect(own.ownService.health).toBe('InternalError'); expect(own.ownService.settlement).toBe('PendingAdjudication')
  expect(JSON.stringify(own)).not.toMatch(/injectionCommand|privateReadContext|flagValue|FlagDerivation|checkerImage/)
})
