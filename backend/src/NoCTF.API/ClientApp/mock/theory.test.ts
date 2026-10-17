import { describe, expect, test } from 'bun:test'
import { createMockApi } from './api'
import { date, id, matchOperation, resolve, responseSchema, type Data } from './schema'

const base = 'http://127.0.0.1:5081'
const practiceId = id(43, 1)
const examId = id(43, 2)
const linkedId = id(43, 3)
const draftId = id(43, 4)
const finishedId = id(43, 5)
const playerId = id(1, 3)
const adminId = id(1)

async function signIn(api: ReturnType<typeof createMockApi>, login: string) {
  const response = await api.handle(new Request(base + '/api/v1/auth/login', { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login, password: 'Mock123!' }) }))
  const { accessToken } = await response.json()
  const send = (path: string, method = 'GET', body?: Data) => api.handle(new Request(base + path, { method,
    headers: { Authorization: 'Bearer ' + accessToken, 'Content-Type': 'application/json' },
    ...(body ? { body: JSON.stringify(body) } : {}) }))
  return { api, accessToken, send }
}

const setup = (login = 'admin') => signIn(createMockApi(), login)

function validate(schema: Data | undefined, value: any, path = '$') {
  if (!schema) return
  if (value === null) { expect(schema.nullable, path).toBe(true); return }
  schema = resolve(schema)
  if (schema.enum) expect(schema.enum, path).toContain(value)
  if (schema.oneOf) return validate(schema.oneOf[0], value, path)
  if (schema.type === 'array') {
    expect(Array.isArray(value), path).toBe(true)
    value.forEach((item: any, index: number) => validate(schema!.items, item, path + '[' + index + ']'))
  }
  if (schema.properties) {
    expect(typeof value, path).toBe('object')
    for (const [key, field] of Object.entries(schema.properties)) validate(field as Data, value[key], path + '.' + key)
    if (schema.additionalProperties === false) expect(Object.keys(value).every(key => key in schema!.properties), path).toBe(true)
  }
  if (['string', 'number', 'boolean'].includes(schema.type)) expect(typeof value, path).toBe(schema.type)
  if (schema.type === 'integer') expect(Number.isInteger(value), path).toBe(true)
}

const questionContent = {
  title: 'Mock 新题 / New question', body: '这是一道本地演示题。', explanation: '演示解析',
  kind: 0, difficulty: 1,
  options: [{ content: 'A 选项', isCorrect: true }, { content: 'B 选项', isCorrect: false }],
  classifications: [{ kind: 0, name: '云安全' }, { kind: 1, name: '基础' }], fileIds: [],
}

async function seedPaper(api: ReturnType<typeof createMockApi>, login: string, activityId: string) {
  const session = await signIn(api, login)
  const issued = await (await session.send('/api/v1/theory/activities/' + activityId + '/session')).json()
  const requestId = crypto.randomUUID()
  const started = await (await session.send('/api/v1/theory/activities/' + activityId + '/papers', 'POST',
    { requestId, expectedIssuedCount: issued.issuedCount })).json()
  return { session, requestId, started }
}

describe('theory Mock fixtures', () => {
  test('theory question bank pages and filters authorized metadata', async () => {
    const { send } = await setup()
    const all = await (await send('/api/v1/theory/questions?offset=0&limit=5&desc=true')).json()
    expect(all.total).toBe(24)
    expect(all.items).toHaveLength(5)
    expect(all.items.every((row: Data) => row.canReadAnswers === true && row.canManage === true)).toBe(true)
    expect(all.items.every((row: Data) => Array.isArray(row.classifications))).toBe(true)

    const kinds = await (await send('/api/v1/theory/questions?kind=1&offset=0&limit=50&desc=true')).json()
    expect(kinds.total).toBe(6)
    expect(kinds.items.every((row: Data) => row.kind === 1)).toBe(true)

    const drafts = await (await send('/api/v1/theory/questions?state=0&offset=0&limit=50&desc=true')).json()
    expect(drafts.total).toBe(2)
    const retired = await (await send('/api/v1/theory/questions?state=2&offset=0&limit=50&desc=true')).json()
    expect(retired.total).toBe(1)

    const knowledge = await (await send('/api/v1/theory/questions?knowledge=' + encodeURIComponent('密码学') + '&offset=0&limit=50&desc=true')).json()
    expect(knowledge.total).toBe(4)
    const tags = await (await send('/api/v1/theory/questions?tag=HTTP&offset=0&limit=50&desc=true')).json()
    expect(tags.total).toBe(2)
    const keyword = await (await send('/api/v1/theory/questions?keyword=AES&offset=0&limit=50&desc=true')).json()
    expect(keyword.total).toBe(1)
    expect(keyword.items[0].title).toContain('AES')
    expect(keyword.items[0].firstExposedAt).toBeString()
    expect(keyword.items[0].paperReferences).toBeGreaterThanOrEqual(0)

    const tail = await (await send('/api/v1/theory/questions?offset=20&limit=10&desc=true')).json()
    expect(tail.total).toBe(24)
    expect(tail.items).toHaveLength(4)

    const player = await signIn(createMockApi(), 'player')
    const useOnly = await (await player.send('/api/v1/theory/questions?offset=0&limit=50&desc=true')).json()
    expect(useOnly.total).toBe(2)
    expect(useOnly.items.map((row: Data) => row.id).sort()).toEqual([id(40, 10), id(40, 5)].sort())
    expect(useOnly.items.find((row: Data) => row.id === id(40, 5)).canReadAnswers).toBe(false)
    expect(useOnly.items.find((row: Data) => row.id === id(40, 10)).canReadAnswers).toBe(true)
    expect(useOnly.items.find((row: Data) => row.id === id(40, 10)).canManage).toBe(false)
  })

  test('theory questions round-trip create, read, update, state, copy and grants', async () => {
    const api = createMockApi()
    const staff = await signIn(api, 'admin')
    const player = await signIn(api, 'player')

    const created = await (await staff.send('/api/v1/theory/questions', 'POST', { content: questionContent })).json()
    expect(created.version).toBe(1)
    expect(created.questionId).toBeString()
    expect(created.sourceId).toBe(created.questionId)

    const read = await (await staff.send('/api/v1/theory/questions/' + created.questionId)).json()
    expect(read.revisionId).toBe(created.revisionId)
    expect(read.content.title).toBe(questionContent.title)
    expect(read.content.options).toHaveLength(2)
    expect(read.content.classifications).toEqual(questionContent.classifications)
    expect(read.question.state).toBe(0)
    expect(read.question.canReadAnswers).toBe(true)
    expect(read.question.canManage).toBe(true)
    expect(read.grants).toEqual([])

    const updated = await (await staff.send('/api/v1/theory/questions/' + created.questionId, 'PUT',
      { content: { ...questionContent, title: 'Mock 更新题 / Updated' } })).json()
    expect(updated.version).toBe(2)
    expect(updated.revisionId).not.toBe(created.revisionId)
    expect((await (await staff.send('/api/v1/theory/questions/' + created.questionId)).json()).content.title).toBe('Mock 更新题 / Updated')

    expect((await staff.send('/api/v1/theory/questions/' + created.questionId + '/state', 'PUT', { state: 1 })).status).toBe(204)
    expect((await (await staff.send('/api/v1/theory/questions/' + created.questionId)).json()).question.state).toBe(1)
    expect((await staff.send('/api/v1/theory/questions/' + created.questionId + '/state', 'PUT', { state: 7 })).status).toBe(400)

    const copy = await (await staff.send('/api/v1/theory/questions/' + created.questionId + '/copy', 'POST')).json()
    expect(copy.questionId).not.toBe(created.questionId)
    expect(copy.sourceId).toBe(created.sourceId)
    const copied = await (await staff.send('/api/v1/theory/questions/' + copy.questionId)).json()
    expect(copied.question.state).toBe(0)
    expect(copied.revisionId).toBe(copy.revisionId)

    expect((await staff.send('/api/v1/theory/questions', 'POST',
      { content: { ...questionContent, options: [{ content: '只有一个', isCorrect: true }] } })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/questions', 'POST', { content: { ...questionContent, title: '   ' } })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/questions', 'POST',
      { content: { ...questionContent, options: [{ content: 'A' }, { content: 'B' }] } })).status).toBe(400)

    expect((await staff.send('/api/v1/theory/questions/' + created.questionId + '/grants/' + playerId, 'PUT', { permission: 0 })).status).toBe(204)
    expect((await staff.send('/api/v1/theory/questions/' + created.questionId + '/grants/' + adminId, 'PUT', { permission: 1 })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/questions/' + created.questionId + '/grants/' + id(99), 'PUT', { permission: 0 })).status).toBe(404)

    expect((await player.send('/api/v1/theory/questions/' + created.questionId)).status).toBe(403)
    const shared = await (await staff.send('/api/v1/theory/questions/' + created.questionId)).json()
    expect(shared.grants).toEqual([{ userId: playerId, name: 'Mock Player', permission: 0 }])
    const playerRow = (await (await player.send('/api/v1/theory/questions?keyword=' + encodeURIComponent('更新题') + '&offset=0&limit=10&desc=true')).json()).items[0]
    expect(playerRow.canReadAnswers).toBe(false)
    expect(playerRow.canManage).toBe(false)

    expect((await staff.send('/api/v1/theory/questions/' + created.questionId + '/grants/' + playerId, 'PUT', { permission: 1 })).status).toBe(204)
    expect((await player.send('/api/v1/theory/questions/' + created.questionId)).status).toBe(200)
    expect((await player.send('/api/v1/theory/questions/' + created.questionId + '/state', 'PUT', { state: 0 })).status).toBe(403)

    expect((await staff.send('/api/v1/theory/questions/' + created.questionId + '/grants/' + playerId, 'PUT', { permission: null })).status).toBe(204)
    expect((await player.send('/api/v1/theory/questions/' + created.questionId)).status).toBe(403)
    expect((await player.send('/api/v1/theory/questions', 'POST', { content: questionContent })).status).toBe(403)
  })

  test('theory activities catalogue, competition link and management reads page and filter', async () => {
    const api = createMockApi()
    const staff = await signIn(api, 'admin')
    const player = await signIn(api, 'player')

    const page = await (await player.send('/api/v1/theory/activities?staff=false&offset=0&limit=2&desc=true')).json()
    expect(page.total).toBe(4)
    expect(page.items).toHaveLength(2)
    expect(page.items[0].id).toBe(linkedId)

    const staffPage = await (await staff.send('/api/v1/theory/activities?staff=true&offset=0&limit=50&desc=true')).json()
    expect(staffPage.total).toBe(5)
    expect(staffPage.items.filter((row: Data) => row.state === 0)).toHaveLength(1)
    expect(staffPage.items.some((row: Data) => row.id === finishedId && row.state === 2)).toBe(true)

    const keyword = await (await staff.send('/api/v1/theory/activities?staff=true&keyword=' + encodeURIComponent('计时') + '&offset=0&limit=50&desc=true')).json()
    expect(keyword.total).toBe(1)
    expect(keyword.items[0].id).toBe(examId)

    const scoped = await (await staff.send('/api/v1/theory/activities?staff=true&competitionId=' + id(2, 1) + '&offset=0&limit=50&desc=true')).json()
    expect(scoped.total).toBe(1)
    expect(scoped.items[0].id).toBe(linkedId)

    const linked = await (await player.send('/api/v1/competitions/' + id(2, 1) + '/theory?staff=false')).json()
    expect(linked).toHaveLength(1)
    expect(linked[0].id).toBe(linkedId)
    expect(linked[0].kind).toBe(2)
    expect(linked[0].questionCount).toBe(8)
    expect(linked[0].maximumPaperPoints).toBe(90)
    expect(linked[0].attemptLimit).toBe(1)
    expect((await (await staff.send('/api/v1/competitions/' + id(2, 1) + '/theory?staff=true')).json())[0].id).toBe(linkedId)
    expect((await player.send('/api/v1/competitions/' + id(2, 1) + '/theory?staff=true')).status).toBe(403)
    expect(await (await player.send('/api/v1/competitions/' + id(2, 2) + '/theory?staff=false')).json()).toEqual([])

    const competition = await (await staff.send('/api/v1/competitions/' + id(2, 1))).json()
    expect(competition.features.hasPublishedTheory).toBe(true)
    expect((await (await staff.send('/api/v1/competitions/' + id(2, 2))).json()).features.hasPublishedTheory).toBe(false)

    const management = await (await staff.send('/api/v1/theory/activities/' + linkedId + '/management')).json()
    expect(management.canManage).toBe(true)
    expect(management.canJudge).toBe(true)
    expect(management.activity.name).toBe('NoCTF 春季挑战赛 · 理论笔试')
    expect(management.rules).toHaveLength(2)
    expect(management.rules[0].filters).toEqual([])
    expect(management.allowExposedQuestions).toBe(true)
    expect(management.issuedCount).toBe(2)
    expect(management.deadline.at).toBeString()
    expect(management.deadline.paused).toBe(false)
    expect(management.competitionEndAt).toBeString()
    expect(management.allowedOperations).toEqual([1, 2, 4, 5, 6])

    const check = await (await staff.send('/api/v1/theory/activities/' + linkedId + '/check', 'POST')).json()
    expect(check.feasible).toBe(true)
    expect(check.rules.map((row: Data) => row.required)).toEqual([6, 2])
    expect(check.rules.every((row: Data) => row.available >= row.required)).toBe(true)

    const audit = await (await staff.send('/api/v1/theory/activities/' + linkedId + '/audit?offset=0&limit=10&desc=true')).json()
    expect(audit.total).toBe(2)
    expect(audit.items[0].kind).toBe(8)
    expect(audit.items[0].actorName).toBe('Mock Admin')
  })

  test('theory papers draw, answer, grade on submit and reject duplicate submission', async () => {
    const api = createMockApi()
    const staff = await signIn(api, 'admin')
    const player = await signIn(api, 'player')

    const empty = await (await player.send('/api/v1/theory/activities/' + linkedId + '/session')).json()
    expect(empty.paper).toBeNull()
    expect(empty.canIssue).toBe(true)
    expect(empty.issuedCount).toBe(0)
    expect(empty.history).toEqual([])
    expect(empty.effectiveScore).toEqual({ points: 0, achievedAt: null, hasSubmission: false })

    const requestId = crypto.randomUUID()
    const issued = await (await player.send('/api/v1/theory/activities/' + linkedId + '/papers', 'POST',
      { requestId, expectedIssuedCount: 0 })).json()
    expect(issued.paper.state).toBe(0)
    expect(issued.paper.attempt).toBe(1)
    expect(issued.paper.questions).toHaveLength(8)
    expect(issued.paper.maximumPoints).toBe(90)
    expect(issued.paper.score).toBeNull()
    expect(issued.paper.questions.every((row: Data) => row.answerRevision && row.options.length >= 2)).toBe(true)
    expect(issued.paper.questions.reduce((total: number, row: Data) => total + row.points, 0)).toBe(90)
    expect(issued.canAnswer).toBe(true)
    expect(issued.canIssue).toBe(false)
    expect(issued.issuedCount).toBe(1)
    expect(issued.deadline.at).toBeString()
    expect(issued.deadline.capped).toBe(false)

    const replay = await (await player.send('/api/v1/theory/activities/' + linkedId + '/papers', 'POST',
      { requestId, expectedIssuedCount: 1 })).json()
    expect(replay.paper.id).toBe(issued.paper.id)
    expect((await player.send('/api/v1/theory/activities/' + linkedId + '/papers', 'POST',
      { requestId: crypto.randomUUID(), expectedIssuedCount: 1 })).status).toBe(409)

    const paperId = issued.paper.id
    const review = await (await staff.send('/api/v1/theory/activities/' + linkedId + '/reviews/' + paperId)).json()
    expect(review.questions).toHaveLength(8)
    expect(review.paper.paperId).toBe(paperId)
    expect(review.paper.name).toBe('Aurora')
    expect(review.paper.teamId).toBe(id(5, 1))
    expect(review.paper.userId).toBeNull()
    expect(review.questions.every((row: Data) => row.correctOptions.length >= 1)).toBe(true)
    expect(review.questions.every((row: Data) => row.result === null)).toBe(true)

    const first = review.questions[0]
    const stale = await player.send('/api/v1/theory/activities/' + linkedId + '/papers/' + paperId + '/answers/' + first.question.id, 'PUT',
      { expectedRevision: crypto.randomUUID(), requestId: crypto.randomUUID(), optionIds: [first.correctOptions[0]] })
    expect(stale.status).toBe(409)
    expect((await stale.json()).code).toBe('AnswerConflict')

    const invalid = await player.send('/api/v1/theory/activities/' + linkedId + '/papers/' + paperId + '/answers/' + first.question.id, 'PUT',
      { expectedRevision: first.question.answerRevision, requestId: crypto.randomUUID(), optionIds: [id(99)] })
    expect(invalid.status).toBe(400)
    expect((await invalid.json()).code).toBe('AnswerInvalid')

    for (const row of review.questions) {
      const answered = await player.send('/api/v1/theory/activities/' + linkedId + '/papers/' + paperId + '/answers/' + row.question.id, 'PUT',
        { expectedRevision: row.question.answerRevision, requestId: crypto.randomUUID(), optionIds: row.correctOptions })
      expect(answered.status).toBe(200)
      const view = await answered.json()
      const item = view.paper.questions.find((candidate: Data) => candidate.id === row.question.id)
      expect(item.options.filter((option: Data) => option.selected).map((option: Data) => option.id).sort()).toEqual([...row.correctOptions].sort())
    }

    const beforeSubmit = await (await player.send('/api/v1/theory/activities/' + linkedId + '/session')).json()
    expect(beforeSubmit.paper.score).toBeNull()
    expect(beforeSubmit.canAnswer).toBe(true)
    expect(beforeSubmit.paper.questions.every((row: Data) => row.answeredByName === 'Mock Player')).toBe(true)

    const submitted = await (await player.send('/api/v1/theory/activities/' + linkedId + '/papers/' + paperId + '/submit', 'POST',
      { expectedRevision: beforeSubmit.paper.answerRevision })).json()
    expect(submitted.paper.state).toBe(1)
    expect(submitted.paper.score).toBe(90)
    expect(submitted.paper.submissionKind).toBe(0)
    expect(submitted.effectiveScore.points).toBe(90)
    expect(submitted.effectiveScore.hasSubmission).toBe(true)
    expect(submitted.history).toHaveLength(1)
    expect(submitted.history[0].score).toBe(90)
    expect(submitted.canAnswer).toBe(false)
    expect(submitted.canIssue).toBe(false)

    const again = await player.send('/api/v1/theory/activities/' + linkedId + '/papers/' + paperId + '/submit', 'POST',
      { expectedRevision: submitted.paper.answerRevision })
    expect(again.status).toBe(409)
    expect((await again.json()).code).toBe('PaperConflict')

    const closedAnswer = await player.send('/api/v1/theory/activities/' + linkedId + '/papers/' + paperId + '/answers/' + first.question.id, 'PUT',
      { expectedRevision: first.question.answerRevision, requestId: crypto.randomUUID(), optionIds: [] })
    expect(closedAnswer.status).toBe(409)
    expect((await closedAnswer.json()).code).toBe('PaperClosed')

    const after = await (await player.send('/api/v1/theory/activities/' + linkedId + '/session')).json()
    expect(after.paper.score).toBe(90)
    expect(after.effectiveScore.points).toBe(90)
    expect(after.issuedCount).toBe(1)

    const board = await (await player.send('/api/v1/theory/activities/' + linkedId + '/leaderboard?offset=0&limit=20&desc=true')).json()
    expect(board.total).toBe(3)
    expect(board.items[0].rank).toBe(1)
    expect(board.items[0].points).toBe(90)
    expect(board.items[0].name).toBe('Aurora')
    expect(board.items.every((row: Data) => row.hasSubmission === true)).toBe(true)
    const boardPage = await (await player.send('/api/v1/theory/activities/' + linkedId + '/leaderboard?offset=1&limit=1&desc=true')).json()
    expect(boardPage.total).toBe(3)
    expect(boardPage.items).toHaveLength(1)
    expect(boardPage.items[0].rank).toBe(2)

    expect((await player.send('/api/v1/theory/activities/' + linkedId + '/reviews?offset=0&limit=10&desc=true')).status).toBe(403)
    expect((await staff.send('/api/v1/theory/activities/' + linkedId + '/reviews/' + id(99))).status).toBe(404)
  })

  test('theory review corrections regrade papers and voiding clears the score', async () => {
    const { api, send } = await setup('admin')
    const rows = await (await send('/api/v1/theory/activities/' + examId + '/reviews?offset=0&limit=20&desc=true')).json()
    expect(rows.total).toBe(6)
    expect(rows.items.every((row: Data) => row.questionCount === 8 && row.state === 1)).toBe(true)

    const row = rows.items[0]
    const review = await (await send('/api/v1/theory/activities/' + examId + '/reviews/' + row.paperId)).json()
    expect(review.paper.participantId).toBe(row.participantId)
    expect(review.paper.effectiveScore.hasSubmission).toBe(true)
    expect(review.questions.map((item: Data) => item.result).some((result: number) => result !== 0)).toBe(true)

    const target = review.questions.find((item: Data) => item.result !== 0)
    const preview = await (await send('/api/v1/theory/activities/' + examId + '/corrections/preview', 'POST',
      { input: { revisionId: target.revisionId, mode: 1, correctOptions: [] } })).json()
    expect(preview.token).toBeString()
    expect(preview.expiresAt).toBeString()
    expect(preview.affectedPapers).toBeGreaterThanOrEqual(1)
    expect(preview.scores).toHaveLength(preview.affectedPapers)
    expect(preview.participants.length).toBeGreaterThanOrEqual(1)
    expect(preview.participants.every((item: Data) => item.after >= item.before)).toBe(true)

    expect((await send('/api/v1/theory/activities/' + examId + '/corrections', 'POST',
      { input: { revisionId: target.revisionId, mode: 0, correctOptions: [] }, token: preview.token, reason: '模式不匹配' })).status).toBe(409)
    expect((await send('/api/v1/theory/activities/' + examId + '/corrections', 'POST',
      { input: { revisionId: target.revisionId, mode: 1, correctOptions: [] }, token: preview.token, reason: '   ' })).status).toBe(400)

    const applied = await send('/api/v1/theory/activities/' + examId + '/corrections', 'POST',
      { input: { revisionId: target.revisionId, mode: 1, correctOptions: [] }, token: preview.token,
        expiresAt: preview.expiresAt, reason: '全量给分演示 / Mock full credit' })
    expect(applied.status).toBe(202)
    const accepted = await applied.json()
    expect(accepted.targetRevision).toBeString()
    expect(accepted.statusUrl).toBe('/api/v1/theory/activities/' + examId + '/management')

    const management = await (await send('/api/v1/theory/activities/' + examId + '/management')).json()
    expect(management.corrections.some((item: Data) => item.revisionId === target.revisionId
      && item.targetRevision === accepted.targetRevision && item.completed === true)).toBe(true)
    expect(management.allowedOperations).toEqual([1, 2, 4, 5, 6])

    const regraded = await (await send('/api/v1/theory/activities/' + examId + '/reviews/' + row.paperId)).json()
    expect(regraded.questions.find((item: Data) => item.revisionId === accepted.targetRevision).result).toBe(3)
    expect(regraded.paper.score).toBeGreaterThanOrEqual(review.paper.score)

    expect((await send('/api/v1/theory/activities/' + examId + '/reviews/' + row.paperId + '/void', 'POST', { reason: '  ' })).status).toBe(400)
    const voided = await send('/api/v1/theory/activities/' + examId + '/reviews/' + row.paperId + '/void', 'POST', { reason: '演示作废 / Mock void' })
    expect(voided.status).toBe(204)
    const afterVoid = await (await send('/api/v1/theory/activities/' + examId + '/reviews/' + row.paperId)).json()
    expect(afterVoid.paper.state).toBe(2)
    expect(afterVoid.paper.score).toBeNull()
    expect(afterVoid.paper.effectiveScore.hasSubmission).toBe(false)
    expect((await send('/api/v1/theory/activities/' + examId + '/reviews/' + row.paperId + '/void', 'POST', { reason: '再次作废' })).status).toBe(409)

    const audit = await (await send('/api/v1/theory/activities/' + examId + '/audit?offset=0&limit=20&desc=true')).json()
    expect(audit.items.some((item: Data) => item.kind === 11 && item.reason === '全量给分演示 / Mock full credit')).toBe(true)
    expect(audit.items.some((item: Data) => item.kind === 9 && item.reason === '演示作废 / Mock void')).toBe(true)
    expect((await (await setup('player')).send('/api/v1/theory/activities/' + examId + '/audit?offset=0&limit=5&desc=true')).status).toBe(403)
    expect(api.state.competitions.length).toBeGreaterThan(0)
  })

  test('theory activity creation, operations and question images round-trip', async () => {
    const api = createMockApi()
    const staff = await signIn(api, 'admin')

    const created = await (await staff.send('/api/v1/theory/activities', 'POST', { activityId: null, competitionId: null,
      input: { name: 'Mock 组合活动', kind: 0, opensAt: date(-1), closesAt: date(72), durationSeconds: 900, attemptLimit: null,
        scoringMode: 0, allowExposedQuestions: false,
        rules: [{ kind: 0, count: 2, points: 10, filters: [{ kind: 0, name: '数字取证' }] }] } })).json()
    expect(created.state).toBe(0)
    expect(created.questionCount).toBe(2)
    expect(created.maximumPaperPoints).toBe(20)
    expect(created.attemptLimit).toBeNull()

    const check = await (await staff.send('/api/v1/theory/activities/' + created.id + '/check', 'POST')).json()
    expect(check.feasible).toBe(true)
    expect(check.rules[0].available).toBeGreaterThanOrEqual(2)
    expect(check.shortages).toEqual([])

    const impossible = await (await staff.send('/api/v1/theory/activities', 'POST', { activityId: null, competitionId: null,
      input: { name: 'Mock 空池活动', kind: 0, opensAt: date(-1), closesAt: date(72), durationSeconds: 900, attemptLimit: null,
        scoringMode: 0, rules: [{ kind: 1, count: 9, points: 10, filters: [] }] } })).json()
    expect((await (await staff.send('/api/v1/theory/activities/' + impossible.id + '/check', 'POST')).json()).feasible).toBe(false)
    expect((await staff.send('/api/v1/theory/activities/' + impossible.id + '/operations', 'POST', { action: 0 })).status).toBe(409)

    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 0 })).status).toBe(200)
    expect((await (await staff.send('/api/v1/theory/activities/' + created.id + '/management')).json()).activity.state).toBe(1)
    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 2 })).status).toBe(200)
    const paused = await (await staff.send('/api/v1/theory/activities/' + created.id + '/management')).json()
    expect(paused.deadline.paused).toBe(true)
    expect(paused.allowedOperations).toEqual([1, 3, 4, 5, 6])
    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 3 })).status).toBe(200)
    expect((await (await staff.send('/api/v1/theory/activities/' + created.id + '/management')).json()).deadline.paused).toBe(false)

    expect((await staff.send('/api/v1/theory/activities', 'POST', { activityId: created.id, competitionId: null,
      input: { name: '已发布改名', kind: 0, opensAt: date(-1), closesAt: date(72), durationSeconds: 900, attemptLimit: null,
        scoringMode: 0, rules: [{ kind: 0, count: 2, points: 10, filters: [] }] } })).status).toBe(409)

    const session = await (await staff.send('/api/v1/theory/activities/' + created.id + '/session')).json()
    expect(session.canIssue).toBe(true)
    const issued = await (await staff.send('/api/v1/theory/activities/' + created.id + '/papers', 'POST',
      { requestId: crypto.randomUUID(), expectedIssuedCount: 0 })).json()
    expect(issued.paper.questions).toHaveLength(2)
    expect(issued.paper.maximumPoints).toBe(20)
    expect(issued.canAnswer).toBe(true)
    const review = await (await staff.send('/api/v1/theory/activities/' + created.id + '/reviews/' + issued.paper.id)).json()
    expect(review.questions).toHaveLength(2)

    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 4 })).status).toBe(200)
    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 0 })).status).toBe(409)
    expect((await (await staff.send('/api/v1/theory/activities/' + created.id + '/management')).json()).activity.issuanceStopped).toBe(true)
    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 6 })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 6, reason: '结束演示' })).status).toBe(200)
    expect((await (await staff.send('/api/v1/theory/activities/' + created.id + '/management')).json()).activity.state).toBe(2)
    expect((await staff.send('/api/v1/theory/activities/' + created.id + '/operations', 'POST', { action: 7, reason: '作废演示' })).status).toBe(200)
    expect((await (await staff.send('/api/v1/theory/activities/' + created.id + '/management')).json()).activity.state).toBe(3)

    const extended = await staff.send('/api/v1/theory/activities/' + practiceId + '/operations', 'POST', { action: 5, deadline: date(24 * 300) })
    expect(extended.status).toBe(200)
    expect((await staff.send('/api/v1/theory/activities/' + practiceId + '/operations', 'POST', { action: 5, deadline: date(-1) })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/activities/' + draftId + '/operations', 'POST', { action: 5, deadline: date(24 * 300) })).status).toBe(409)
    expect((await staff.send('/api/v1/theory/activities/' + linkedId + '/operations', 'POST', { action: 5, deadline: date(24 * 300) })).status).toBe(409)
    expect((await staff.send('/api/v1/theory/activities/' + finishedId + '/operations', 'POST', { action: 1 })).status).toBe(409)

    const question = await (await staff.send('/api/v1/theory/questions', 'POST', { content: questionContent })).json()
    const form = new FormData()
    form.set('file', new File(['mock theory image'], 'theory.png', { type: 'image/png' }))
    const uploaded = await api.handle(new Request(base + '/api/v1/theory/questions/' + question.questionId + '/images',
      { method: 'POST', headers: { Authorization: 'Bearer ' + staff.accessToken }, body: form }))
    expect(uploaded.status).toBe(200)
    const result = await uploaded.json()
    expect(result.fileId).toBeString()
    expect(result.failure).toBeNull()

    const image = await staff.send('/api/v1/theory/questions/' + question.questionId + '/images/' + result.fileId)
    expect(image.status).toBe(200)
    expect(image.headers.get('content-type')).toContain('image/png')
    expect((await image.arrayBuffer()).byteLength).toBeGreaterThan(0)
    expect((await (await staff.send('/api/v1/theory/questions/' + question.questionId)).json()).content.fileIds).toEqual([result.fileId])
    expect((await staff.send('/api/v1/theory/questions/' + question.questionId + '/images/' + id(99))).status).toBe(404)

    const rejected = new FormData()
    rejected.set('file', new File(['not an image'], 'theory.txt', { type: 'text/plain' }))
    const bad = await api.handle(new Request(base + '/api/v1/theory/questions/' + question.questionId + '/images',
      { method: 'POST', headers: { Authorization: 'Bearer ' + staff.accessToken }, body: rejected }))
    expect(bad.status).toBe(400)
    expect((await bad.json()).code).toBe('UnsupportedFormat')
  })

  test('theory permissions, unknown identifiers and the real-time invalidation channel', async () => {
    const api = createMockApi()
    const anonymous = createMockApi()
    expect((await anonymous.handle(new Request(base + '/api/v1/theory/activities?staff=false&offset=0&limit=10&desc=true'))).status).toBe(401)
    expect((await anonymous.handle(new Request(base + '/api/v1/theory/questions?offset=0&limit=10&desc=true'))).status).toBe(401)

    const staff = await signIn(api, 'admin')
    const player = await signIn(api, 'player')
    expect((await player.send('/api/v1/theory/activities?staff=true&offset=0&limit=10&desc=true')).status).toBe(403)
    expect((await player.send('/api/v1/theory/activities/' + linkedId + '/management')).status).toBe(403)
    expect((await player.send('/api/v1/theory/activities/' + linkedId + '/check', 'POST')).status).toBe(403)
    expect((await player.send('/api/v1/theory/activities/' + linkedId + '/audit?offset=0&limit=10&desc=true')).status).toBe(403)
    expect((await player.send('/api/v1/theory/activities/' + linkedId + '/corrections/preview', 'POST',
      { input: { revisionId: id(41, 1), mode: 1, correctOptions: [] } })).status).toBe(403)
    expect((await player.send('/api/v1/theory/questions/' + id(40, 1))).status).toBe(403)
    expect((await player.send('/api/v1/theory/questions/' + id(40, 5))).status).toBe(403)
    expect((await player.send('/api/v1/theory/questions/' + id(40, 10))).status).toBe(200)
    expect((await player.send('/api/v1/theory/questions/' + id(40, 10) + '/copy', 'POST')).status).toBe(403)
    expect((await player.send('/api/v1/theory/activities/' + finishedId + '/session')).status).toBe(200)
    expect((await player.send('/api/v1/theory/activities/' + draftId + '/session')).status).toBe(404)
    expect((await staff.send('/api/v1/theory/activities/' + draftId + '/session')).status).toBe(200)
    expect((await staff.send('/api/v1/theory/activities/' + finishedId + '/papers', 'POST',
      { requestId: crypto.randomUUID(), expectedIssuedCount: 0 })).status).toBe(409)

    expect((await staff.send('/api/v1/theory/questions/' + id(99))).status).toBe(404)
    expect((await staff.send('/api/v1/theory/questions/' + id(99) + '/copy', 'POST')).status).toBe(404)
    expect((await staff.send('/api/v1/theory/activities/' + id(99) + '/management')).status).toBe(404)
    expect((await staff.send('/api/v1/theory/activities/' + id(99) + '/leaderboard?offset=0&limit=5&desc=true')).status).toBe(404)
    expect((await staff.send('/api/v1/theory/activities/' + linkedId + '/operations', 'POST', { action: 9 })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/activities/' + linkedId + '/operations', 'POST', { action: 1 })).status).toBe(200)
    expect((await staff.send('/api/v1/theory/activities/' + linkedId + '/operations', 'POST', { action: 1 })).status).toBe(409)
    expect((await staff.send('/api/v1/theory/activities', 'POST', { activityId: null, competitionId: null, input: { name: '', rules: [] } })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/activities', 'POST', { activityId: null, competitionId: null,
      input: { name: '范围错误', kind: 0, opensAt: date(10), closesAt: date(5), durationSeconds: 600, attemptLimit: null, scoringMode: 0,
        rules: [{ kind: 0, count: 1, points: 10, filters: [] }] } })).status).toBe(400)
    expect((await staff.send('/api/v1/theory/activities', 'POST', { activityId: null, competitionId: null,
      input: { name: '无限次考试', kind: 1, opensAt: date(1), closesAt: date(10), durationSeconds: 600, attemptLimit: null, scoringMode: 0,
        rules: [{ kind: 0, count: 1, points: 10, filters: [] }] } })).status).toBe(400)

    // Real-time theory invalidation follows the same local-only channel as notifications.
    const notified: string[] = []
    api.theoryChanges.add(activityId => notified.push(activityId))
    const { session } = await seedPaper(api, 'player', practiceId)
    expect(notified).toContain(practiceId)
    await session.send('/api/v1/theory/activities', 'POST', { activityId: null, competitionId: null,
      input: { name: '通知活动', kind: 0, opensAt: date(-1), closesAt: date(24), durationSeconds: 600, attemptLimit: null, scoringMode: 0,
        rules: [{ kind: 0, count: 1, points: 5, filters: [] }] } })
    expect(notified).toContain(practiceId)
  })

  test('theory responses match the current OpenAPI shape', async () => {
    const api = createMockApi()
    const staff = await signIn(api, 'admin')
    const linkedRows = await (await staff.send('/api/v1/theory/activities/' + linkedId + '/reviews?offset=0&limit=1&desc=true')).json()
    const paperId = linkedRows.items[0].paperId
    const review = await (await staff.send('/api/v1/theory/activities/' + linkedId + '/reviews/' + paperId)).json()
    const revisionId = review.questions[0].revisionId

    const checks: Array<{ path: string; method?: string; body?: Data }> = [
      { path: '/api/v1/theory/questions?offset=0&limit=3&desc=true' },
      { path: '/api/v1/theory/questions/' + id(40, 1) },
      { path: '/api/v1/theory/activities?staff=true&offset=0&limit=3&desc=true' },
      { path: '/api/v1/theory/activities?staff=false&offset=0&limit=3&desc=true' },
      { path: '/api/v1/theory/activities/' + linkedId + '/management' },
      { path: '/api/v1/theory/activities/' + linkedId + '/check', method: 'POST' },
      { path: '/api/v1/theory/activities/' + linkedId + '/audit?offset=0&limit=3&desc=true' },
      { path: '/api/v1/theory/activities/' + linkedId + '/session' },
      { path: '/api/v1/theory/activities/' + examId + '/leaderboard?offset=0&limit=3&desc=true' },
      { path: '/api/v1/theory/activities/' + linkedId + '/reviews?offset=0&limit=3&desc=true' },
      { path: '/api/v1/theory/activities/' + linkedId + '/reviews/' + paperId },
      { path: '/api/v1/theory/activities/' + linkedId + '/reviews/' + paperId + '/consultations', method: 'POST', body: { body: '请解释评分标准 / Explain the scoring' } },
      { path: '/api/v1/theory/activities/' + linkedId + '/corrections/preview', method: 'POST', body: { input: { revisionId, mode: 1, correctOptions: [] } } },
      { path: '/api/v1/theory/activities/' + draftId + '/operations', method: 'POST', body: { action: 0 } },
      { path: '/api/v1/competitions/' + id(2, 1) + '/theory?staff=true' },
      { path: '/api/v1/theory/questions/' + id(40, 1) + '/copy', method: 'POST' },
    ]
    for (const item of checks) {
      const match = matchOperation(item.path.split('?')[0]!, item.method ?? 'GET')
      expect(match, item.path).not.toBeNull()
      const response = await staff.send(item.path, item.method ?? 'GET', item.body)
      expect(response.status, item.path).toBe(200)
      if (response.headers.get('Content-Type')?.includes('json'))
        validate(responseSchema(match!.operation, '200'), await response.json(), item.path)
    }

    const issuedResponse = await staff.send('/api/v1/theory/activities/' + practiceId + '/papers', 'POST',
      { requestId: crypto.randomUUID(), expectedIssuedCount: 0 })
    expect(issuedResponse.status).toBe(200)
    const view = await issuedResponse.json()
    validate(responseSchema(matchOperation('/api/v1/theory/activities/{activityId}/papers', 'POST')!.operation, '200'), view, 'session')
    expect(view.paper.questions.length).toBe(6)
    const first = view.paper.questions[0]
    const answered = await staff.send('/api/v1/theory/activities/' + practiceId + '/papers/' + view.paper.id + '/answers/' + first.id, 'PUT',
      { expectedRevision: first.answerRevision, requestId: crypto.randomUUID(), optionIds: [first.options[0].id] })
    expect(answered.status).toBe(200)
    const answeredView = await answered.json()
    validate(responseSchema(matchOperation('/api/v1/theory/activities/{activityId}/papers/{paperId}/answers/{itemId}', 'PUT')!.operation, '200'), answeredView, 'answer')
    const submitted = await staff.send('/api/v1/theory/activities/' + practiceId + '/papers/' + view.paper.id + '/submit', 'POST',
      { expectedRevision: answeredView.paper.answerRevision })
    expect(submitted.status).toBe(200)
    const submittedView = await submitted.json()
    validate(responseSchema(matchOperation('/api/v1/theory/activities/{activityId}/papers/{paperId}/submit', 'POST')!.operation, '200'), submittedView, 'submit')
    expect(submittedView.paper.state).toBe(1)
    expect(submittedView.effectiveScore.hasSubmission).toBe(true)
  })
})
