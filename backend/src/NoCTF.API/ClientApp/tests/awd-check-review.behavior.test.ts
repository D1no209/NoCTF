import { expect, test } from 'bun:test'
import { computed, effectScope, nextTick, reactive, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { awdEvidenceCanBeReviewed, awdCheckLabel } from '../app/features/admin/awd/awd-check-evidence'
import { parseApiError } from '../app/utils/api-error'
import { createMockApi } from '../mock/api'
import { id } from '../mock/schema'

const source = await Bun.file(new URL('../app/features/admin/awd/useAwdCheckReview.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const complete = { gameplayFactId: 'first', settlement: 'PendingAdjudication' as const, attemptId: 'attempt', manifestComplete: true, evidenceComplete: true,
  attemptStartedAt: '2026-10-13T00:00:00Z', attemptDeadline: '2026-10-13T00:00:30Z', observedAt: '2026-10-13T00:00:20Z', receivedAt: '2026-10-13T00:01:10Z' }
function harness() {
  const props = reactive({ competitionId: 'competition', canJudge: true, teams: [], challenges: [] })
  const reads: Array<{ path: { gameplayFactId: string }; resolve: (value: object) => void }> = []
  const writes: Array<{ body: object; resolve: (value: object) => void }> = []
  const lists: object[] = []
  let unmount = () => {}
  const deps = { computed, ref, watch, useOffsetPagination, awdCheckLabel, awdEvidenceCanBeReviewed, parseApiError,
    onMounted: (callback: () => void) => callback(), onUnmounted: (callback: () => void) => { unmount = callback },
    message: (key: string) => ({ key }), translate: (key: string) => key, toast: { success: () => {} },
    adminListAwdChecks: async ({ query }: { query: object }) => { lists.push(query); return { data: { items: [], total: 0 } } },
    adminGetAwdCheckEvidence: ({ path }: { path: { gameplayFactId: string } }) => new Promise(resolve => reads.push({ path, resolve })),
    adminAdjudicateAwdCheck: ({ body }: { body: object }) => new Promise(resolve => writes.push({ body, resolve })),
  }
  const scope = effectScope()
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAwdCheckReview;`)(deps)
  const state = scope.run(() => factory(props))!
  return { state, props, reads, writes, lists, stop() { unmount(); scope.stop() } }
}

test('a late receipt may be reviewed, but missing or out-of-attempt observations may not', () => {
  expect(awdEvidenceCanBeReviewed(complete)).toBeTrue()
  for (const changes of [{ observedAt: null }, { observedAt: '2026-10-13T00:00:31Z' }, { observedAt: 'invalid' }, { manifestComplete: false }, { evidenceComplete: false }, { attemptId: null }])
    expect(awdEvidenceCanBeReviewed({ ...complete, ...changes })).toBeFalse()
})

test('switching and closing check evidence discards late responses', async () => {
  const app = harness()
  try {
    app.state.openEvidence({ gameplayFactId: 'first' })
    app.state.openEvidence({ gameplayFactId: 'second' })
    app.reads[1]!.resolve({ data: { ...complete, gameplayFactId: 'second' } }); await drain()
    app.reads[0]!.resolve({ data: complete }); await drain()
    expect(app.state.evidence.value.gameplayFactId).toBe('second')
    app.state.loadEvidence(); app.state.detailOpen.value = false; await drain()
    app.reads[2]!.resolve({ data: complete }); await drain()
    expect(app.state.evidence.value).toBeNull()
  }
  finally { app.stop() }
})

test('adjudication requires permission and a reason, sends displayed evidence identity, and suppresses duplicate clicks', async () => {
  const app = harness()
  try {
    app.state.openEvidence({ gameplayFactId: 'first' }); app.reads[0]!.resolve({ data: complete }); await drain()
    await app.state.adjudicate(); expect(app.writes).toHaveLength(0)
    app.state.reason.value = '  Verified original observation  '
    app.props.canJudge = false; await app.state.adjudicate(); expect(app.writes).toHaveLength(0)
    app.props.canJudge = true
    const action = app.state.adjudicate(); await app.state.adjudicate()
    expect(app.writes).toHaveLength(1)
    expect(app.writes[0]!.body).toEqual({ action: 'AcceptEvidence', reason: 'Verified original observation', expectedAttemptId: complete.attemptId, expectedReceivedAt: complete.receivedAt })
    app.writes[0]!.resolve({ data: { ...complete, settlement: 'Final' } }); await action
    expect(app.state.canAdjudicate.value).toBeFalse()
    expect(app.state.evidence.value.settlement).toBe('Final')
  }
  finally { app.stop() }
})

test('failed adjudication requires evidence reload; filter changes reset pagination and close old evidence', async () => {
  const app = harness()
  try {
    app.state.openEvidence({ gameplayFactId: 'first' }); app.reads[0]!.resolve({ data: complete }); await drain()
    app.state.reason.value = 'Evidence reviewed'
    const action = app.state.adjudicate(); app.writes[0]!.resolve({ error: { status: 409 } }); await action
    expect(app.state.needsReload.value).toBeTrue(); expect(app.state.canAdjudicate.value).toBeFalse()
    app.state.loadEvidence(); app.reads[1]!.resolve({ data: { ...complete, attemptId: 'new-attempt' } }); await drain()
    expect(app.state.needsReload.value).toBeFalse()
    app.state.team.value = 'other-team'; await drain()
    expect(app.state.detailOpen.value).toBeFalse()
    expect(app.state.page.value).toBe(1)
    expect(app.lists.at(-1)).toMatchObject({ teamId: 'other-team', settlement: 2, offset: 0 })
  }
  finally { app.stop() }
})

test('local AWD review flow preserves unknown evidence on exemption and rejects a repeated decision', async () => {
  const api = createMockApi(), origin = 'http://127.0.0.1:5081'
  const login = await api.handle(new Request(origin + '/api/v1/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'admin', password: 'Mock123!' }) }))
  const { accessToken } = await login.json()
  const send = (path: string, method = 'GET', body?: object) => api.handle(new Request(origin + path, { method,
    headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' }, ...(body ? { body: JSON.stringify(body) } : {}) }))
  const base = `/api/v1/admin/competitions/${id(2, 2)}/awd/checks`
  const response = await send(base + '?allSettlements=false&offset=0&limit=10&desc=true')
  expect(response.status).toBe(200)
  const list = await response.json()
  expect(list.items).toHaveLength(2)
  const item = list.items.find((item: { reason: string }) => item.reason === 'CallbackMissing')
  const evidence = await (await send(`${base}/${item.gameplayFactId}`)).json()
  expect(evidence.observedAt).toBeNull()
  const body = { expectedAttemptId: evidence.attemptId, expectedReceivedAt: evidence.receivedAt, action: 'Exempt', reason: 'Runner evidence never arrived' }
  const applied = await (await send(`${base}/${item.gameplayFactId}/adjudication`, 'POST', body)).json()
  expect(applied.settlement).toBe('Exempted'); expect(applied.health).toBe('InternalError')
  expect((await send(`${base}/${item.gameplayFactId}/adjudication`, 'POST', body)).status).toBe(409)
  expect((await (await send(base + '?allSettlements=false&offset=0&limit=10&desc=true')).json()).total).toBe(1)
})
