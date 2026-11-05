import { describe, expect, test } from 'bun:test'
import { createMockApi } from './api'
import { id, matchOperation, resolve, responseSchema, type Data } from './schema'

const base = 'http://127.0.0.1:5081'
const root = '/api/v1/questionnaires/activities'
const practice = `${root}/${id(43)}`
const exam = `${root}/${id(43, 2)}`
const bank = `/api/v1/admin/challenges/${id(39)}`

async function signIn(api: ReturnType<typeof createMockApi>, login: string) {
  const response = await api.handle(new Request(`${base}/api/v1/auth/login`, { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login, password: 'Mock123!' }) }))
  const { accessToken } = await response.json()
  const send = (path: string, method = 'GET', body?: Data | FormData) => api.handle(new Request(base + path, { method,
    headers: { Authorization: `Bearer ${accessToken}`, ...(body instanceof FormData ? {} : { 'Content-Type': 'application/json' }) },
    ...(body ? { body: body instanceof FormData ? body : JSON.stringify(body) } : {}) }))
  return { send }
}
async function setup() {
  const api = createMockApi()
  return { api, admin: await signIn(api, 'admin'), player: await signIn(api, 'player'), organizer: await signIn(api, 'organizer') }
}
async function issue(send: Awaited<ReturnType<typeof signIn>>['send'], path: string) {
  const current = await (await send(`${path}/session`)).json()
  const body = { requestId: crypto.randomUUID(), expectedIssuedCount: current.issuedCount }
  const response = await send(`${path}/sessions`, 'POST', body)
  expect(response.status).toBe(200)
  return { view: await response.json(), body }
}
function validate(schema: Data | undefined, value: any, path: string) {
  if (!schema) return
  if (value === null) { expect(schema.nullable, path).toBe(true); return }
  schema = resolve(schema)
  if (schema.enum) expect(schema.enum, path).toContain(value)
  if (schema.oneOf) return validate(schema.oneOf[0], value, path)
  if (schema.type === 'array') {
    expect(Array.isArray(value), path).toBe(true)
    value.forEach((item: any, index: number) => validate(schema!.items, item, `${path}[${index}]`))
  }
  if (schema.properties) for (const [key, field] of Object.entries(schema.properties)) validate(field as Data, value[key], `${path}.${key}`)
  if (['string', 'number', 'boolean'].includes(schema.type)) expect(typeof value, path).toBe(schema.type)
  if (schema.type === 'integer') expect(Number.isInteger(value), path).toBe(true)
}

describe('questionnaire demo', () => {
  test('bank lists reusable multi-question capabilities with no competition mode', async () => {
    const { admin, player } = await setup()
    const list = await (await admin.send('/api/v1/admin/challenges?requiredCapabilities=Questionnaire&offset=0&limit=50')).json()
    expect(list.total).toBe(6)
    expect(list.items.every((item: Data) => item.capabilities.includes('Questionnaire') && !('mode' in item))).toBe(true)
    const template = await (await admin.send(bank)).json()
    expect(template.definition.runtime).toBeNull()
    expect(template.definition.capabilities[0].questionnaire.questions).toHaveLength(4)
    const activities = await (await player.send(root)).json()
    expect(activities.items).toHaveLength(3)
    expect(activities.items.every((activity: Data) => [0, 1].includes(activity.kind) && !('competitionId' in activity))).toBe(true)
    expect((await player.send(`/api/v1/competitions/${id(2)}/theory`)).status).toBe(404)
    expect((await admin.send('/api/v1/theory/questions')).status).toBe(404)
  })

  test('draw, save, reconnect and submit preserve receipts and user ownership', async () => {
    const { admin, player, organizer } = await setup()
    const { view, body } = await issue(player.send, practice)
    expect(view.session.questions).toHaveLength(5)
    const serialized = JSON.stringify(view)
    for (const privateField of ['isCorrect', 'correctOptions', 'explanation']) expect(serialized).not.toContain(privateField)
    expect((await (await player.send(`${practice}/sessions`, 'POST', body)).json()).session.id).toBe(view.session.id)
    expect((await organizer.send(`${practice}/sessions/${view.session.id}`)).status).toBe(404)
    const question = view.session.questions[0]
    const answer = { expectedRevision: question.answerRevision, requestId: crypto.randomUUID(), optionIds: [question.options[0].id] }
    const path = `${practice}/sessions/${view.session.id}/answers/${question.id}`
    const saved = await (await player.send(path, 'PUT', answer)).json()
    const replayed = await (await player.send(path, 'PUT', answer)).json()
    expect(replayed.session.answerRevision).toBe(saved.session.answerRevision)
    expect((await player.send(path, 'PUT', { ...answer, optionIds: [] })).status).toBe(409)
    expect((await (await player.send(`${practice}/session`)).json()).session.questions[0].options[0].selected).toBe(true)
    const submitted = await (await player.send(`${practice}/sessions/${view.session.id}/submit`, 'POST', { expectedRevision: saved.session.answerRevision })).json()
    expect(submitted.session.score).toBe(10)
    expect(submitted.session.questions).toEqual([])
    const duplicate = await (await player.send(`${practice}/sessions/${view.session.id}/submit`, 'POST', { expectedRevision: saved.session.answerRevision })).json()
    expect(duplicate.session).toEqual(submitted.session)
    expect(duplicate.issuedCount).toBe(1)
    expect((await (await admin.send(`${practice}/reviews`)).json()).total).toBe(1)
  })

  test('fixed questionnaires and published candidate versions survive template edits', async () => {
    const { admin, player, organizer } = await setup()
    const first = await issue(player.send, exam)
    const before = await (await admin.send(bank)).json()
    const definition = structuredClone(before.definition)
    definition.capabilities[0].questionnaire.questions[0].body = 'New content after publication'
    expect((await admin.send(bank, 'PATCH', { content: { definition } })).status).toBe(200)
    const second = await issue(organizer.send, exam)
    expect(second.view.session.questions[0].body).toBe(first.view.session.questions[0].body)
    expect(second.view.session.questions).toHaveLength(4)
    const current = await (await admin.send(bank)).json()
    expect(current.definition.capabilities[0].questionnaire.revisionId).not.toBe(before.definition.capabilities[0].questionnaire.revisionId)
    expect((await (await player.send(`${exam}/session`)).json()).session.questions[0].body).toBe(first.view.session.questions[0].body)
  })

  test('independent activity lifecycle supports fixed selection, pause, extension and ending', async () => {
    const { admin, player } = await setup()
    const created = await (await admin.send(root, 'POST', { input: { name: 'Fixed questionnaire', kind: 0,
      opensAt: new Date(Date.now() - 3600000).toISOString(), closesAt: new Date(Date.now() + 3600000).toISOString(),
      durationSeconds: 600, attemptLimit: null, scoringMode: 0, allowExposedQuestions: true, selectionKind: 1,
      fixedQuestionnaires: [{ challengeId: id(39, 2), pointsPerQuestion: 25 }], rules: [] } })).json()
    const path = `${root}/${created.id}`
    expect((await (await admin.send(`${path}/check`, 'POST')).json()).feasible).toBe(true)
    expect((await admin.send(`${path}/operations`, 'POST', { action: 0 })).status).toBe(200)
    expect((await (await admin.send(`${path}/management`)).json()).selectionKind).toBe(1)
    const { view } = await issue(player.send, path)
    expect(view.session.maximumPoints).toBe(100)
    await admin.send(`${path}/operations`, 'POST', { action: 2 })
    expect((await (await player.send(`${path}/session`)).json()).canAnswer).toBe(false)
    await admin.send(`${path}/operations`, 'POST', { action: 3 })
    expect((await (await player.send(`${path}/session`)).json()).canAnswer).toBe(true)
    await admin.send(`${path}/operations`, 'POST', { action: 5, deadline: new Date(Date.now() + 7200000).toISOString() })
    await admin.send(`${path}/operations`, 'POST', { action: 6, reason: 'End demo' })
    const ended = await (await player.send(`${path}/session`)).json()
    expect(ended.session.state).toBe(1)
    expect(ended.session.submissionKind).toBe(2)
    expect(ended.canIssue).toBe(false)
    expect((await (await admin.send(`${path}/audit`)).json()).total).toBe(5)
  })

  test('corrections update the selected question, future sessions and the examination leaderboard', async () => {
    const { admin, player, organizer } = await setup()
    const { view } = await issue(player.send, exam)
    await player.send(`${exam}/sessions/${view.session.id}/submit`, 'POST', { expectedRevision: view.session.answerRevision })
    const review = await (await admin.send(`${exam}/reviews/${view.session.id}`)).json()
    const input = { revisionId: review.questions[0].revisionId, mode: 1, correctOptions: [] }
    const preview = await (await admin.send(`${exam}/corrections/preview`, 'POST', { input })).json()
    expect(preview.affectedSessions).toBe(1)
    expect(preview.scores[0].after).toBe(10)
    expect((await admin.send(`${exam}/corrections`, 'POST', { input, token: preview.token, expiresAt: preview.expiresAt, reason: 'Full credit for demo' })).status).toBe(202)
    expect((await (await player.send(`${exam}/session`)).json()).effectiveScore.points).toBe(10)
    const future = await issue(organizer.send, exam)
    const submitted = await (await organizer.send(`${exam}/sessions/${future.view.session.id}/submit`, 'POST', { expectedRevision: future.view.session.answerRevision })).json()
    expect(submitted.session.score).toBe(10)
    const leaderboard = await (await player.send(`${exam}/leaderboard`)).json()
    expect(leaderboard.items.every((row: Data) => !('teamId' in row))).toBe(true)
    expect((await player.send(`${practice}/leaderboard`)).status).toBe(404)
    expect((await admin.send(`${exam}/reviews/${view.session.id}/void`, 'POST', { reason: 'Void demo session' })).status).toBe(204)
    expect((await (await player.send(`${exam}/session`)).json()).effectiveScore.points).toBe(0)
  })

  test('questionnaire images use bank ownership and immutable issued references', async () => {
    const { admin, player } = await setup()
    const form = new FormData()
    form.set('file', new Blob([new Uint8Array([137, 80, 78, 71])], { type: 'image/png' }), 'demo.png')
    const uploadPath = `${bank}/questionnaire/questions/${id(40)}/images`
    expect((await player.send(uploadPath, 'POST', form)).status).toBe(403)
    const uploaded = await admin.send(uploadPath, 'POST', form)
    expect(uploaded.status).toBe(200)
    const { fileId } = await uploaded.json()
    expect((await admin.send(`${bank}/questionnaire/images/${fileId}`)).status).toBe(200)
    expect((await admin.send(`/api/v1/admin/challenges/${id(39, 2)}/questionnaire/images/${fileId}`)).status).toBe(404)
    const { view } = await issue(player.send, exam)
    expect((await player.send(`${exam}/sessions/${view.session.id}/images/${fileId}`)).status).toBe(404)
  })

  test('current activity, session and review responses satisfy OpenAPI', async () => {
    const { admin, player } = await setup()
    const { view } = await issue(player.send, practice)
    for (const path of [root, `${practice}/management`, `${practice}/session`, `${practice}/reviews`, `${practice}/reviews/${view.session.id}`, `${exam}/leaderboard`, `${practice}/audit`]) {
      const response = await admin.send(path)
      expect(response.status, path).toBe(200)
      const operation = matchOperation(path, 'GET')!
      validate(responseSchema(operation.operation), await response.json(), path)
    }
  })

  test('bank copies preserve questionnaire source identities with independent content', async () => {
    const { admin } = await setup()
    const original = await (await admin.send(bank)).json()
    const copied = await admin.send('/api/v1/admin/challenges/copies', 'POST', { sourceChallengeId: id(39), copyAttachments: false, copyFlags: false })
    expect(copied.status).toBe(200)
    const receipt = await copied.json()
    expect(receipt.canonicalChallengeId).toBe(id(39))
    const copy = await (await admin.send(`/api/v1/admin/challenges/${receipt.challengeId}`)).json()
    const sourceQuestionnaire = original.definition.capabilities[0].questionnaire
    const questionnaire = copy.definition.capabilities[0].questionnaire
    expect(questionnaire.revisionId).not.toBe(sourceQuestionnaire.revisionId)
    expect(questionnaire.questions[0].id).not.toBe(sourceQuestionnaire.questions[0].id)
    expect(questionnaire.questions[0].sourceId).toBe(sourceQuestionnaire.questions[0].sourceId)
    questionnaire.questions[0].body = 'Independent content'
    await admin.send(`/api/v1/admin/challenges/${receipt.challengeId}`, 'PATCH', { content: { definition: copy.definition } })
    expect((await (await admin.send(bank)).json()).definition).toEqual(original.definition)
  })
})
