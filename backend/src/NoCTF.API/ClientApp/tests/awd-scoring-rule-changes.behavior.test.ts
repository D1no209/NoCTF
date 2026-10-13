import { expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, reactive, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { awdScoringFields, awdScoringKeys, awdScoringPolicy, awdScoringValues, awdPolicyRows } from '../app/features/admin/awd/awd-scoring-policy'
import { awdFieldVisible, awdInvalidFields } from '../app/utils/awd-config-fields'
import { parseApiError } from '../app/utils/api-error'
import { createMockApi } from '../mock/api'
import { id } from '../mock/schema'

const source = await Bun.file(new URL('../app/features/admin/awd/useAwdScoringRuleChanges.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const issued = awdScoringPolicy(awdScoringValues({}))
const response = () => ({ data: { roundNumber: 4, policies: [{ rulesId: 'rules', revision: 1, issuedPolicy: issued, effectivePolicy: issued }], recentChanges: [] } })
const impact = (token = 'preview', lifetime = 120000) => ({ data: { token, projectedAt: new Date().toISOString(), expiresAt: new Date(Date.now() + lifetime).toISOString(),
  affectedFlags: 2, affectedRuleSnapshots: 1, teams: Array.from({ length: 23 }, (_, index) => ({ teamId: `team-${index}`, scoreBefore: 1000, scoreAfter: 1100 })) } })
async function harness() {
  const props = reactive({ competitionId: 'competition', competitionChallengeId: 'challenge', canWrite: true })
  const previews: Array<{ body: object; resolve: (value: object) => void }> = [], writes: Array<{ body: object; resolve: (value: object) => void }> = []
  let unmount = () => {}
  const deps = { computed, ref, watch, markRaw, useOffsetPagination, awdScoringFields, awdScoringPolicy, awdScoringValues, awdPolicyRows, awdFieldVisible, awdInvalidFields, parseApiError,
    ConfigFieldInputComponent: {}, DefinitionSectionComponent: {},
    onMounted: (callback: () => void) => callback(), onUnmounted: (callback: () => void) => { unmount = callback },
    message: (key: string) => ({ key }), translate: (key: string) => key, toast: { success: () => {} },
    adminGetAwdScoringRules: async () => response(),
    adminPreviewAwdScoringRules: ({ body }: { body: object }) => new Promise(resolve => previews.push({ body, resolve })),
    adminApplyAwdScoringRules: ({ body }: { body: object }) => new Promise(resolve => writes.push({ body, resolve })),
  }
  const scope = effectScope()
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAwdScoringRuleChanges;`)(deps)
  const state = scope.run(() => factory(props))!
  await drain()
  return { state, props, previews, writes, stop() { unmount(); scope.stop() } }
}

test('explicit scoring payload includes all policy keys, zero values and no flag/execution/preset settings', () => {
  const values = awdScoringValues({ attackPoints: 0, maximumRoundPenalty: 0 })
  const policy = awdScoringPolicy({ ...values, preset: 'Scarcity', flagLifetime: 50, checkerIntervalSeconds: 9 })
  expect(Object.keys(policy).sort()).toEqual([...awdScoringKeys].sort())
  expect(policy.attackPoints).toBe(0); expect(policy.maximumRoundPenalty).toBe(0)
  expect(awdInvalidFields(awdScoringFields(), values)).toEqual([])
  expect(awdInvalidFields(awdScoringFields(), { ...values, attackRewardMode: 'Scarcity', attackSettlement: 'Immediate' })).toContain('attackSettlement')
})

test('a reason, permission and valid explicit range are needed before preview', async () => {
  const app = await harness()
  try {
    expect(app.state.canPreview.value).toBeFalse()
    app.state.reason.value = 'Reviewed scoring correction'; expect(app.state.canPreview.value).toBeTrue()
    app.state.firstRound.value = 5; expect(app.state.canPreview.value).toBeFalse()
    app.state.firstRound.value = 1; app.state.timing.value = 'Immediately'; expect(app.state.canPreview.value).toBeFalse()
    app.state.firstRound.value = 4; expect(app.state.canPreview.value).toBeTrue()
    app.props.canWrite = false; expect(app.state.canPreview.value).toBeFalse()
  }
  finally { app.stop() }
})

test('edits invalidate both an in-flight and completed preview', async () => {
  const app = await harness()
  try {
    app.state.reason.value = 'Correction'
    const first = app.state.previewChanges()
    app.state.updateField('attackPoints', 75)
    app.previews[0]!.resolve(impact('obsolete')); await first
    expect(app.state.preview.value).toBeNull(); expect(app.state.canApply.value).toBeFalse()
    const second = app.state.previewChanges(); app.previews[1]!.resolve(impact('current')); await second
    expect(app.state.canApply.value).toBeTrue()
    app.state.reason.value = 'Changed reason'
    expect(app.state.previewOpen.value).toBeFalse(); expect(app.state.canApply.value).toBeFalse()
    await app.state.applyChanges(); expect(app.writes).toHaveLength(0)
  }
  finally { app.stop() }
})

test('apply uses the reviewed request and token once, with all team impacts pageable', async () => {
  const app = await harness()
  try {
    app.state.reason.value = '  Corrected score budget  '
    app.state.updateField('attackPoints', 75)
    const preview = app.state.previewChanges(); app.previews[0]!.resolve(impact()); await preview
    expect(app.state.impactTotal.value).toBe(23)
    expect(app.state.impactItems.value).toHaveLength(10)
    await app.state.loadImpactPage(3); expect(app.state.impactItems.value).toHaveLength(3)
    const apply = app.state.applyChanges(); await app.state.applyChanges()
    expect(app.writes).toHaveLength(1)
    expect(app.writes[0]!.body).toEqual({ ...app.previews[0]!.body, previewToken: 'preview' })
    app.writes[0]!.resolve({ data: { eventId: 'audit-event' } }); await apply
    expect(app.state.preview.value).toBeNull(); expect(app.state.canApply.value).toBeFalse()
  }
  finally { app.stop() }
})

test('expired preview and server rejection never allow reusing the old token', async () => {
  const app = await harness()
  try {
    app.state.reason.value = 'Correction'
    const expired = app.state.previewChanges(); app.previews[0]!.resolve(impact('old', -1)); await expired
    expect(app.state.previewExpired.value).toBeTrue(); expect(app.state.canApply.value).toBeFalse()
    const valid = app.state.previewChanges(); app.previews[1]!.resolve(impact()); await valid
    const apply = app.state.applyChanges(); app.writes[0]!.resolve({ error: { status: 409 } }); await apply
    expect(app.state.preview.value).toBeNull(); expect(app.state.actionError.value).not.toBeNull()
  }
  finally { app.stop() }
})

test('local scoring flow preserves issued policy and records the explicit range, reason and applied policy', async () => {
  const api = createMockApi(), origin = 'http://127.0.0.1:5081'
  const login = await api.handle(new Request(origin + '/api/v1/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'admin', password: 'Mock123!' }) }))
  const { accessToken } = await login.json()
  const send = (path: string, method = 'GET', body?: object) => api.handle(new Request(origin + path, { method,
    headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' }, ...(body ? { body: JSON.stringify(body) } : {}) }))
  const base = `/api/v1/admin/competitions/${id(2, 2)}/challenges/${id(4, 101)}/awd/`
  const body = { firstRound: 2, lastRound: 4, timing: 'RecalculateHistory', reason: 'Budget correction', policy: { ...issued, attackPoints: 75 } }
  const preview = await (await send(base + 'scoring-preview', 'POST', body)).json()
  expect(preview.teams.length).toBeGreaterThan(0)
  expect((await send(base + 'scoring', 'POST', { ...body, previewToken: preview.token })).status).toBe(200)
  const read = await (await send(base + 'scoring-rules?roundNumber=3')).json()
  expect(read.policies[0].issuedPolicy.attackPoints).toBe(50)
  expect(read.policies[0].effectivePolicy.attackPoints).toBe(75)
  expect(read.recentChanges[0]).toMatchObject({ firstRound: 2, lastRound: 4, reason: 'Budget correction' })
  expect((await send(base + 'scoring', 'POST', { ...body, previewToken: preview.token })).status).toBe(409)
})
