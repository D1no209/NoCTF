import { describe, expect, test } from 'bun:test'
import { createMockApi } from './api'
import { id } from './schema'

const base = 'http://127.0.0.1:5081'
const bank = `/api/v1/admin/challenges/${id(3)}`
async function setup(login = 'admin') {
  const api = createMockApi()
  const response = await api.handle(new Request(`${base}/api/v1/auth/login`, { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login, password: 'Mock123!' }) }))
  const { accessToken } = await response.json()
  const send = (path: string, method = 'POST', body?: Record<string, unknown> | FormData) => api.handle(new Request(base + path, {
    method, headers: { Authorization: `Bearer ${accessToken}`, ...(body instanceof FormData ? {} : { 'Content-Type': 'application/json' }) },
    ...(body ? { body: body instanceof FormData ? body : JSON.stringify(body) } : {}),
  }))
  return { api, send }
}
function upload(requestId = crypto.randomUUID(), content = 'demo archive') {
  const form = new FormData()
  form.set('requestId', requestId)
  form.set('file', new File([content], 'fix.tar.gz', { type: 'application/gzip' }))
  return form
}

describe('capability tests demo', () => {
  test('Break and Questionnaire return objective results without changing formal facts', async () => {
    const { api, send } = await setup()
    const before = await (await api.handle(new Request(`${base}/__mock/status`))).json()
    expect((await (await send(`${bank}/break-tests`, 'POST', { flag: 'flag{mock_success}' })).json()).match).toBe('Matched')
    expect((await (await send(`${bank}/break-tests`, 'POST', { flag: 'wrong' })).json()).match).toBe('NotMatched')
    const questionnaire = `/api/v1/admin/challenges/${id(39)}`
    const template = await (await send(questionnaire, 'GET')).json()
    const question = template.definition.capabilities[0].questionnaire.questions[0]
    const result = await (await send(`${questionnaire}/questionnaire-tests`, 'POST', { answers: [{ questionId: question.id,
      selectedOptionIds: question.options.filter((option: any) => option.isCorrect).map((option: any) => option.id) }] })).json()
    expect(result.questions[0].result).toBe('Correct')
    expect((await api.handle(new Request(`${base}/__mock/status`))).status).toBe(200)
    expect((await (await api.handle(new Request(`${base}/__mock/status`))).json()).facts).toBe(before.facts)
  })

  test('Fix uses independent targets, restores status and verifies request replay', async () => {
    const { send } = await setup()
    const requestId = crypto.randomUUID()
    const first = await send(`${bank}/fix-tests`, 'POST', upload(requestId))
    expect(first.status).toBe(202)
    const accepted = await first.json()
    expect(await (await send(`${bank}/fix-tests`, 'POST', upload(requestId))).json()).toEqual(accepted)
    expect((await send(`${bank}/fix-tests`, 'POST', upload(requestId, 'changed'))).status).toBe(409)
    const current = await (await send(accepted.statusUrl, 'GET')).json()
    expect(current.state).toBe('Completed')
    expect(current.fixResult).toBe('Applied')
    expect(current.checkCompleted).toBe(false)
    const combined = await (await send(`${bank}/fix-and-check-tests`, 'POST', upload())).json()
    expect(combined.runtimeInstanceId).not.toBe(accepted.runtimeInstanceId)
    const combinedStatus = await (await send(combined.statusUrl, 'GET')).json()
    expect(combinedStatus.checkCompleted).toBe(true)
    expect(combinedStatus.probes.length).toBeGreaterThan(0)
    expect((await send(accepted.statusUrl.replace(id(3), id(3, 2)), 'GET')).status).toBe(404)
  })

  test('Check and Control use the template interactive runtime and preserve it', async () => {
    const { send } = await setup()
    await send(`${bank}/test-runtimes`, 'POST', {})
    const runtime = await (await send(`${bank}/test-runtimes/current`, 'GET')).json()
    expect((await send(`${bank}/check-tests`, 'POST', { requestId: crypto.randomUUID(), runtimeInstanceId: crypto.randomUUID() })).status).toBe(409)
    const check = await send(`${bank}/check-tests`, 'POST', { requestId: crypto.randomUUID(), runtimeInstanceId: runtime.id })
    expect(check.status).toBe(202)
    expect((await (await send(`${bank}/control-tests`, 'POST', { runtimeInstanceId: runtime.id })).json()).result).toBe('Uncontrolled')
    expect((await (await send(`${bank}/test-runtimes/current`, 'GET')).json()).id).toBe(runtime.id)
    expect((await send(`${bank}/questionnaire-tests`, 'POST', { answers: [] })).status).toBe(400)
    const { send: player } = await setup('player')
    expect((await player(`${bank}/break-tests`, 'POST', { flag: 'flag{mock_success}' })).status).toBe(403)
  })
})
