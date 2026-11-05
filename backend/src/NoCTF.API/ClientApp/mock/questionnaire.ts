import { date, id, now, type Data } from './schema'
import type { MockState } from './data/fixtures'

const kinds = ['SingleChoice', 'MultipleChoice', 'TrueFalse']
const difficulties = ['Basic', 'Intermediate', 'Advanced']

export function createMockQuestionnaire(context: {
  state: MockState
  json: (value: unknown, status?: number, headers?: HeadersInit) => Response
  shape: (schema: Data | undefined, value: any) => any
  responseSchema: (operation: Data, status?: string) => Data | undefined
}) {
  const { state, json, shape, responseSchema } = context
  const activities = new Map<string, Data>()
  const papers = new Map<string, Data>()
  const questionVersions = new Map<string, string>()
  const images = new Map<string, { challengeId: string; blob: Blob }>()
  const issueReceipts = new Map<string, string>()
  const answerReceipts = new Map<string, { itemId: string; options: string[] }>()
  const previews = new Map<string, Data>()
  const changes = new Set<(activityId: string) => void>()
  const staff = (user?: Data) => ['Administrator', 'Organizer'].includes(user?.role)
  const fail = (status: number, detail: string, code?: string) => json({ status, title: 'Mock API', detail, code }, status)
  const respond = (operation: Data, value: unknown, status = 200) => status === 204
    ? new Response(null, { status }) : json(shape(responseSchema(operation, String(status)), value), status, { 'Cache-Control': 'private,no-store' })
  const changed = (activityId: string) => { for (const notify of changes) notify(activityId) }
  const nameOf = (userId: string) => state.users.find(user => user.userId === userId)?.userName ?? 'Demo participant'
  const equal = (left: string[], right: string[]) => left.length === right.length && new Set(left).size === left.length && left.every(value => right.includes(value))
  const page = (rows: Data[], url: URL) => {
    const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0))
    const limit = Math.max(1, Math.min(200, Number(url.searchParams.get('limit') ?? 20)))
    return { items: rows.slice(offset, offset + limit), total: rows.length }
  }

  const seeds = [
    ['网络安全基础', 'CIA 中完整性的含义是什么？', '防止未授权修改', '提高压缩率'],
    ['密码学', 'HMAC 提供什么保护？', '完整性与来源认证', '数据压缩'],
    ['Web 安全', '参数化查询主要防御什么？', 'SQL 注入', '流量拥塞'],
    ['逆向工程', 'ELF 头中哪个字段表示入口？', 'e_entry', 'e_version'],
    ['数字取证', '证据镜像应当如何保护？', '保留原始内容与摘要', '直接修改原始文件'],
    ['云安全', '最小权限要求什么？', '按任务授予必要权限', '共用管理员密钥'],
  ]
  for (const [index, seed] of seeds.entries()) {
    const questions = Array.from({ length: 4 }, (_, order) => {
      const kind = order % 3
      const questionId = id(40, index * 4 + order + 1)
      const options = kind === 2 ? [
        { id: id(42, index * 40 + order * 4 + 1), content: 'True', isCorrect: true, truthValue: true },
        { id: id(42, index * 40 + order * 4 + 2), content: 'False', isCorrect: false, truthValue: false },
      ] : [
        { id: id(42, index * 40 + order * 4 + 1), content: seed[2], isCorrect: true, truthValue: null },
        { id: id(42, index * 40 + order * 4 + 2), content: seed[3], isCorrect: false, truthValue: null },
        ...(kind === 1 ? [{ id: id(42, index * 40 + order * 4 + 3), content: '定期核查相关安全配置', isCorrect: true, truthValue: null }] : []),
      ]
      return { id: questionId, sourceId: questionId, kind: kinds[kind], difficulty: difficulties[order % 3],
        title: `${seed[0]} ${order + 1}`, body: kind === 2 ? `${seed[2]}属于合理的安全实践。` : seed[1],
        explanation: seed[2], options, classifications: [{ kind: 'Knowledge', name: seed[0] }], fileIds: [] }
    })
    state.templates.push({ id: id(39, index + 1), ownerId: id(1), managerIds: [], visibility: 'Shared',
      title: `${seed[0]}问卷`, description: '独立考试和练习的演示问卷。', direction: 'Misc', capabilities: ['Questionnaire'],
      definition: { runtime: null, capabilities: [{ kind: 'Questionnaire', questionnaire: { revisionId: id(41, index + 1), revisionNumber: 1, questions } }] },
      activeCompetitionReferenceCount: 0, createdAt: date(-168), updatedAt: date(-3) })
  }
  const catalog = () => state.templates.flatMap(template => template.deletedAt ? [] : template.definition.capabilities
    .filter((capability: Data) => capability.kind === 'Questionnaire').map((capability: Data) => ({ challengeId: template.id, ...capability.questionnaire })))
  const versionId = (revisionId: string, questionId: string) => {
    const key = `${revisionId}:${questionId}`
    if (!questionVersions.has(key)) questionVersions.set(key, crypto.randomUUID())
    return questionVersions.get(key)!
  }
  const pool = () => catalog().flatMap(revision => revision.questions.map((question: Data) => ({
    ...structuredClone(question), challengeId: revision.challengeId, questionnaireRevisionId: revision.revisionId,
    revisionId: versionId(revision.revisionId, question.id),
    kind: kinds.indexOf(question.kind), difficulty: difficulties.indexOf(question.difficulty),
    classifications: question.classifications.map((entry: Data) => ({ ...entry, kind: entry.kind === 'Knowledge' ? 0 : 1 })),
    options: question.options.map((option: Data) => ({ ...option })),
  })))
  const selectPool = (activity: Data, rule: Data) => (activity.pool ?? pool()).filter((question: Data) => question.kind === rule.kind
    && (rule.difficulty == null || question.difficulty === rule.difficulty)
    && rule.filters.every((filter: Data) => question.classifications.some((entry: Data) => entry.kind === filter.kind && entry.name === filter.name)))
  const fixedPool = (activity: Data) => activity.fixedQuestionnaires.flatMap((fixed: Data) => (activity.pool ?? pool())
    .filter((question: Data) => question.challengeId === fixed.challengeId).map((question: Data) => ({ question, points: fixed.pointsPerQuestion })))
  const count = (activity: Data) => activity.selectionKind === 1 ? fixedPool(activity).length : activity.rules.reduce((total: number, rule: Data) => total + rule.count, 0)
  const maximum = (activity: Data) => activity.selectionKind === 1 ? fixedPool(activity).reduce((total: number, item: Data) => total + item.points, 0)
    : activity.rules.reduce((total: number, rule: Data) => total + rule.count * rule.points, 0)
  const summary = (activity: Data) => ({ id: activity.id, name: activity.name, kind: activity.kind, state: activity.state,
    opensAt: activity.opensAt, closesAt: activity.closesAt, durationSeconds: activity.durationSeconds, attemptLimit: activity.attemptLimit,
    scoringMode: activity.scoringMode, maximumSessionPoints: maximum(activity), questionCount: count(activity), issuanceStopped: activity.issuanceStopped })
  const capacity = (activity: Data) => {
    const rules = activity.selectionKind === 1 ? activity.fixedQuestionnaires.map((fixed: Data, order: number) => ({
      order, required: 1, available: fixedPool(activity).some((item: Data) => item.question.challengeId === fixed.challengeId) ? 1 : 0,
    })) : activity.rules.map((rule: Data, order: number) => ({ order, required: rule.count, available: selectPool(activity, rule).length }))
    return { feasible: rules.length > 0 && rules.every((rule: Data) => rule.available >= rule.required), rules,
      shortages: rules.filter((rule: Data) => rule.available < rule.required).map((rule: Data) => ({ ruleId: id(48, rule.order + 1), required: rule.required, allocated: rule.available })) }
  }
  const ownPapers = (activityId: string, userId: string) => [...papers.values()].filter(paper => paper.activityId === activityId && paper.userId === userId)
  const effective = (activity: Data, userId: string) => {
    const submitted = ownPapers(activity.id, userId).filter(paper => paper.state === 1)
    const chosen = activity.scoringMode === 1 ? submitted.at(-1) : submitted.toSorted((a, b) => b.score - a.score)[0]
    return { points: activity.scoringMode === 2 ? submitted.reduce((total, paper) => total + paper.score, 0) : chosen?.score ?? 0,
      achievedAt: chosen?.submittedAt ?? null, hasSubmission: submitted.length > 0 }
  }
  const deadline = (activity: Data, paper?: Data) => ({ at: new Date(Math.min(Date.parse(activity.closesAt), paper
    ? Date.parse(paper.openedAt) + activity.durationSeconds * 1000 + activity.extensionMs : Infinity)).toISOString(),
    extension: `${Math.floor(activity.extensionMs / 3600000).toString().padStart(2, '0')}:00:00`, paused: activity.paused, capped: false })
  const operations = (activity: Data) => activity.state === 0 ? [0] : activity.state === 1
    ? [...([...papers.values()].some(paper => paper.activityId === activity.id) ? [] : [1]), activity.paused ? 3 : 2, 4, 5, 6, 7] : []
  const audit = (activity: Data, kind: number, user: Data, reason: string | null) => activity.audit.unshift({
    id: crypto.randomUUID(), kind, actorName: user.userName, occurredAt: now(), reason })
  const grade = (paper: Data) => {
    for (const item of paper.items) item.result = item.fullCredit ? 3 : !item.selected.length ? 2
      : equal(item.selected, item.options.filter((option: Data) => option.isCorrect).map((option: Data) => option.id)) ? 0 : 1
    paper.score = paper.items.reduce((total: number, item: Data) => total + ([0, 3].includes(item.result) ? item.points : 0), 0)
  }
  const itemView = (item: Data) => ({ id: item.id, answerRevision: item.answerRevision, order: item.order, kind: item.kind,
    body: item.body, points: item.points, answeredById: item.answeredById, answeredByName: item.answeredByName, answeredAt: item.answeredAt,
    options: item.options.map((option: Data, order: number) => ({ id: option.id, order,
    content: option.content, truthValue: option.truthValue, selected: item.selected.includes(option.id) })) })
  const paperView = (paper: Data) => ({ id: paper.id, answerRevision: paper.answerRevision, attempt: paper.attempt, state: paper.state,
    openedAt: paper.openedAt, submittedAt: paper.submittedAt, submissionKind: paper.submissionKind, score: paper.score,
    maximumPoints: paper.items.reduce((total: number, item: Data) => total + item.points, 0),
    questions: paper.state === 0 ? paper.items.map(itemView) : [] })
  const session = (activity: Data, user: Data, selected?: Data) => {
    const own = ownPapers(activity.id, user.userId)
    const paper = selected ?? own.at(-1)
    const open = activity.state === 1 && !activity.paused && Date.now() >= Date.parse(activity.opensAt) && Date.now() < Date.parse(activity.closesAt)
    return { activity: summary(activity), deadline: deadline(activity, paper), session: paper ? paperView(paper) : null, issuedCount: own.length,
      effectiveScore: effective(activity, user.userId), history: own.map(candidate => ({ sessionId: candidate.id, attempt: candidate.attempt,
        state: candidate.state, score: candidate.score, submittedAt: candidate.submittedAt })), serverTime: now(),
      canIssue: open && !activity.issuanceStopped && paper?.state !== 0 && (activity.attemptLimit == null || own.length < activity.attemptLimit),
      canAnswer: open && paper?.state === 0 && Date.now() < Date.parse(deadline(activity, paper).at) }
  }
  const review = (activity: Data, paper: Data) => ({ sessionId: paper.id, participantId: paper.userId, userId: paper.userId,
    name: nameOf(paper.userId), attempt: paper.attempt, state: paper.state, openedAt: paper.openedAt, submittedAt: paper.submittedAt,
    score: paper.score, questionCount: paper.items.length, answeredCount: paper.items.filter((item: Data) => item.selected.length).length,
    submissionKind: paper.submissionKind, deadline: deadline(activity, paper), effectiveScore: effective(activity, paper.userId) })
  const reviewItem = (item: Data) => ({ question: itemView(item), revisionId: item.revisionId,
    correctOptions: item.options.filter((option: Data) => option.isCorrect).map((option: Data) => option.id), explanation: item.explanation,
    result: item.result == null ? null : ['Correct', 'Wrong', 'Unanswered', 'FullCredit'][item.result] })
  const issue = (activity: Data, userId: string) => {
    const used = new Set<string>()
    const seen = new Set(ownPapers(activity.id, userId).flatMap(paper => paper.items.map((item: Data) => item.sourceId)))
    const drawn = activity.selectionKind === 1 ? fixedPool(activity) : activity.rules.flatMap((rule: Data) => selectPool(activity, rule)
      .filter((question: Data) => !used.has(question.sourceId) && !seen.has(question.sourceId))
      .slice(0, rule.count).map((question: Data) => { used.add(question.sourceId); return { question, points: rule.points } }))
    if (drawn.length !== count(activity)) return null
    const paper: Data = { id: crypto.randomUUID(), activityId: activity.id, userId, attempt: ownPapers(activity.id, userId).length + 1,
      state: 0, openedAt: now(), submittedAt: null, submissionKind: null, score: null, answerRevision: crypto.randomUUID(),
      items: drawn.map(({ question, points }: Data, order: number) => ({ ...structuredClone(question), sourceId: question.sourceId,
        id: crypto.randomUUID(), order, points, selected: [], answerRevision: crypto.randomUUID(), result: null,
        answeredById: null, answeredByName: null, answeredAt: null, fullCredit: question.fullCredit ?? false })) }
    papers.set(paper.id, paper)
    return paper
  }

  for (const [index, seed] of [
    { name: '问卷练习 · 网络安全基础', kind: 0, state: 1, selectionKind: 0 },
    { name: '独立考试 · 计时测验', kind: 1, state: 1, selectionKind: 1 },
    { name: '问卷练习 · 草稿', kind: 0, state: 0, selectionKind: 0 },
    { name: '独立考试 · 已结束', kind: 1, state: 2, selectionKind: 1 },
  ].entries()) {
    const activity: Data = { id: id(43, index + 1), ...seed, opensAt: date(-24), closesAt: date(index === 3 ? -1 : 96),
      durationSeconds: 1800, attemptLimit: seed.kind === 0 ? null : 3, scoringMode: 0, allowExposedQuestions: true,
      rules: seed.selectionKind === 0 ? [{ kind: 0, difficulty: null, count: 3, points: 10, filters: [] }, { kind: 1, difficulty: null, count: 2, points: 20, filters: [] }] : [],
      fixedQuestionnaires: seed.selectionKind === 1 ? [{ challengeId: id(39), pointsPerQuestion: 10 }] : [],
      paused: false, pausedAt: null, extensionMs: 0, issuanceStopped: seed.state === 2, corrections: [], audit: [], pool: null }
    if (activity.state !== 0) activity.pool = structuredClone(pool())
    activities.set(activity.id, activity)
  }

  async function handle(request: Request, route: string, p: Data, body: Data, user: Data | undefined, operation: Data): Promise<Response | null> {
    const imageRoute = route.startsWith('/admin/challenges/{challengeId}/questionnaire/')
    if (!route.startsWith('/questionnaires/activities') && !imageRoute) return null
    if (!user) return fail(401, 'Sign in to the demo site')
    const url = new URL(request.url)
    if (imageRoute) {
      if (!staff(user)) return fail(403, 'Staff account required')
      const template = state.templates.find(template => template.id === p.challengeId)
      const questionnaire = template?.definition.capabilities.find((capability: Data) => capability.kind === 'Questionnaire')?.questionnaire
      if (!questionnaire) return fail(404, 'Questionnaire not found')
      if (request.method === 'POST') {
        const question = questionnaire.questions.find((question: Data) => question.id === p.questionId)
        if (!question) return fail(404, 'Question not found')
        const file = (await request.formData()).get('file')
        if (!(file instanceof Blob) || !['image/png', 'image/jpeg', 'image/webp'].includes(file.type)) return fail(400, 'Unsupported image format')
        if (file.size > 5 * 1024 * 1024) return fail(413, 'Image exceeds 5 MiB')
        const fileId = crypto.randomUUID()
        images.set(fileId, { challengeId: p.challengeId, blob: file })
        question.fileIds.push(fileId)
        questionnaire.revisionId = crypto.randomUUID(); questionnaire.revisionNumber++
        return respond(operation, { fileId, revisionId: questionnaire.revisionId })
      }
      const image = images.get(p.fileId)
      return image && questionnaire.questions.some((question: Data) => question.fileIds.includes(p.fileId)) ? new Response(image.blob) : fail(404, 'Image not found')
    }
    if (route === '/questionnaires/activities') {
      if (request.method === 'GET') {
        if (url.searchParams.get('staff') === 'true' && !staff(user)) return fail(403, 'Staff account required')
        const keyword = (url.searchParams.get('keyword') ?? '').toLowerCase()
        return respond(operation, page([...activities.values()].filter(activity => (staff(user) && url.searchParams.get('staff') === 'true' || activity.state === 1 || activity.state === 2)
          && activity.name.toLowerCase().includes(keyword)).map(summary), url))
      }
      if (!staff(user)) return fail(403, 'Staff account required')
      const input = body.input ?? {}
      if (!input.name?.trim() || ![0, 1].includes(input.kind) || Date.parse(input.closesAt) <= Date.parse(input.opensAt)
        || !Number.isFinite(Date.parse(input.closesAt)) || !Number.isFinite(Date.parse(input.opensAt))
        || input.durationSeconds < 60 || input.attemptLimit != null && input.attemptLimit < 1
        || input.attemptLimit == null && (input.kind !== 0 || input.scoringMode === 2)) return fail(400, 'Invalid activity configuration')
      const existing = body.activityId ? activities.get(body.activityId) : null
      if (body.activityId && !existing) return fail(404, 'Activity not found')
      if (existing && existing.state !== 0) return fail(409, 'Published configuration is frozen', 'ActivityFrozen')
      const activity = { ...structuredClone(input), id: existing?.id ?? crypto.randomUUID(), state: 0, pool: null,
        paused: false, pausedAt: null, extensionMs: 0, issuanceStopped: false, corrections: [], audit: [],
        rules: (input.rules ?? []).map((rule: Data) => ({ ...rule, filters: rule.filters ?? [] })), fixedQuestionnaires: input.fixedQuestionnaires ?? [] }
      if (![0, 1].includes(activity.selectionKind) || !count(activity)) return fail(400, 'Questionnaire selection is required')
      activities.set(activity.id, activity)
      changed(activity.id)
      return respond(operation, summary(activity))
    }
    const activity = activities.get(p.activityId)
    if (!activity || !staff(user) && [0, 3].includes(activity.state)) return fail(404, 'Activity not found')
    const suffix = route.slice('/questionnaires/activities/{activityId}'.length)
    if (['/management', '/check', '/operations', '/audit', '/reviews', '/reviews/{sessionId}', '/reviews/{sessionId}/void', '/corrections/preview', '/corrections'].includes(suffix) && !staff(user)) return fail(403, 'Staff account required')
    if (suffix === '/management') return respond(operation, { activity: summary(activity), rules: activity.rules,
      selectionKind: activity.selectionKind, fixedQuestionnaires: activity.fixedQuestionnaires, allowExposedQuestions: activity.allowExposedQuestions,
      canManage: true, canJudge: true, deadline: deadline(activity), corrections: activity.corrections,
      issuedCount: [...papers.values()].filter(paper => paper.activityId === activity.id).length, allowedOperations: operations(activity) })
    if (suffix === '/check') return respond(operation, capacity(activity))
    if (suffix === '/audit') return respond(operation, page(activity.audit, url))
    if (suffix === '/operations') {
      const action = Number(body.action)
      if (!operations(activity).includes(action)) return fail(409, 'Operation unavailable', 'OperationUnavailable')
      if (action >= 6 && !body.reason?.trim()) return fail(400, 'Reason required')
      if (action === 0) {
        if (!capacity(activity).feasible) return fail(409, 'Insufficient questions', 'InsufficientQuestions')
        activity.pool = structuredClone(pool()); activity.state = 1
      } else if (action === 1) { activity.state = 0; activity.pool = null }
      else if (action === 2) { activity.paused = true; activity.pausedAt = Date.now() }
      else if (action === 3) { activity.extensionMs += Date.now() - activity.pausedAt; activity.paused = false; activity.pausedAt = null }
      else if (action === 4) activity.issuanceStopped = true
      else if (action === 5) {
        if (!Number.isFinite(Date.parse(body.deadline)) || Date.parse(body.deadline) <= Date.parse(activity.closesAt)) return fail(400, 'Deadline must extend the current one')
        activity.closesAt = body.deadline
      } else { activity.state = action === 6 ? 2 : 3; activity.issuanceStopped = true }
      if (action === 6) for (const paper of papers.values()) if (paper.activityId === activity.id && paper.state === 0) {
        paper.state = 1; paper.submittedAt = now(); paper.submissionKind = 2; grade(paper)
      }
      if (action === 7) for (const paper of papers.values()) if (paper.activityId === activity.id) { paper.state = 2; paper.score = null }
      audit(activity, action, user, body.reason ?? null); changed(activity.id)
      return respond(operation, summary(activity))
    }
    if (suffix === '/session') return respond(operation, session(activity, user))
    if (suffix === '/sessions') {
      const key = `${activity.id}:${user.userId}:${body.requestId}`
      if (issueReceipts.has(key)) return respond(operation, session(activity, user, papers.get(issueReceipts.get(key)!)))
      if (body.expectedIssuedCount !== ownPapers(activity.id, user.userId).length) return fail(409, 'Issued count changed', 'IssueConflict')
      if (!session(activity, user).canIssue) return fail(409, 'Issuance unavailable', 'IssuanceStopped')
      const paper = issue(activity, user.userId)
      if (!paper) return fail(409, 'Insufficient questions', 'InsufficientQuestions')
      issueReceipts.set(key, paper.id); changed(activity.id)
      return respond(operation, session(activity, user))
    }
    if (suffix === '/reviews') return respond(operation, page([...papers.values()].filter(paper => paper.activityId === activity.id).map(paper => review(activity, paper)), url))
    if (suffix === '/leaderboard') {
      if (activity.kind !== 1) return fail(404, 'An examination is required')
      const rows = [...new Set([...papers.values()].filter(paper => paper.activityId === activity.id).map(paper => paper.userId))]
        .map(userId => ({ userId, name: nameOf(userId), ...effective(activity, userId) })).sort((a, b) => b.points - a.points)
      return respond(operation, page(rows.map((row, index) => ({ rank: index + 1, ...row })), url))
    }
    if (suffix.startsWith('/corrections')) {
      const input = body.input ?? {}
      const targets = (activity.pool ?? []).filter((question: Data) => question.revisionId === input.revisionId)
      if (!targets.length) return fail(404, 'Questionnaire revision not found')
      if (suffix === '/corrections/preview') {
        const valid = new Set(targets.flatMap((question: Data) => question.options.map((option: Data) => option.id)))
        if (![0, 1].includes(input.mode) || !Array.isArray(input.correctOptions)
          || new Set(input.correctOptions).size !== input.correctOptions.length
          || input.mode === 1 && input.correctOptions.length !== 0
          || input.mode === 0 && (targets[0].kind === 1 ? input.correctOptions.length < 2 : input.correctOptions.length !== 1)
          || input.correctOptions.some((option: string) => !valid.has(option))) return fail(400, 'Invalid correction')
        const affected = [...papers.values()].filter(paper => paper.activityId === activity.id && paper.state === 1
          && paper.items.some((item: Data) => item.revisionId === input.revisionId))
        const scores = affected.map(paper => {
          const copy = structuredClone(paper)
          for (const item of copy.items) if (item.revisionId === input.revisionId) {
            item.fullCredit = input.mode === 1
            if (input.mode === 0) item.options.forEach((option: Data) => { option.isCorrect = input.correctOptions.includes(option.id) })
          }
          grade(copy)
          return { sessionId: paper.id, participantId: paper.userId, before: paper.score ?? 0, after: copy.score }
        })
        const token = crypto.randomUUID()
        const expiresAt = Date.now() + 300000
        previews.set(token, { input: structuredClone(input), activityId: activity.id, expiresAt })
        return respond(operation, { token, expiresAt: new Date(expiresAt).toISOString(), affectedSessions: affected.length, scores, participants: [] })
      }
      const preview = previews.get(body.token)
      if (!preview || preview.activityId !== activity.id || preview.expiresAt <= Date.now() || Date.parse(body.expiresAt) !== preview.expiresAt
        || JSON.stringify(preview.input) !== JSON.stringify(input)) return fail(409, 'Correction preview expired', 'PreviewChanged')
      if (!body.reason?.trim()) return fail(400, 'Reason required')
      for (const question of targets) {
        question.fullCredit = input.mode === 1
        if (input.mode === 0) question.options.forEach((option: Data) => { option.isCorrect = input.correctOptions.includes(option.id) })
      }
      for (const paper of papers.values()) if (paper.activityId === activity.id) {
        for (const item of paper.items) if (item.revisionId === input.revisionId) {
          item.fullCredit = input.mode === 1
          if (input.mode === 0) item.options.forEach((option: Data) => { option.isCorrect = input.correctOptions.includes(option.id) })
        }
        if (paper.state === 1) grade(paper)
      }
      const targetRevision = crypto.randomUUID()
      activity.corrections.unshift({ revisionId: input.revisionId, targetRevision, completed: true })
      previews.delete(body.token); audit(activity, input.mode === 1 ? 11 : 10, user, body.reason); changed(activity.id)
      return respond(operation, { targetRevision, statusUrl: `/api/v1/questionnaires/activities/${activity.id}/management` }, 202)
    }
    const paper = papers.get(p.sessionId)
    if (!paper || paper.activityId !== activity.id) return fail(404, 'Paper not found')
    if (!staff(user) && paper.userId !== user.userId) return fail(403, 'Paper belongs to another participant')
    if (suffix.startsWith('/sessions/') && paper.userId !== user.userId) return fail(404, 'Session not found')
    if (suffix === '/reviews/{sessionId}') return respond(operation, { session: review(activity, paper), questions: paper.items.map(reviewItem) })
    if (suffix === '/reviews/{sessionId}/void') {
      if (!body.reason?.trim()) return fail(400, 'Reason required')
      if (paper.state === 2) return fail(409, 'Paper already voided', 'PaperVoided')
      paper.state = 2; paper.score = null; audit(activity, 9, user, body.reason); changed(activity.id)
      return respond(operation, null, 204)
    }
    if (suffix === '/sessions/{sessionId}') return respond(operation, session(activity, user, paper))
    if (suffix === '/sessions/{sessionId}/images/{fileId}') {
      const image = images.get(p.fileId)
      return image && paper.items.some((item: Data) => item.fileIds.includes(p.fileId))
        ? new Response(image.blob) : fail(404, 'Image not found')
    }
    if (paper.userId !== user.userId) return fail(403, 'Paper belongs to another participant')
    if (suffix === '/sessions/{sessionId}/submit') {
      if (paper.state === 1) return respond(operation, session(activity, user))
      if (paper.state !== 0 || paper.answerRevision !== body.expectedRevision) return fail(409, 'Session changed', 'SessionConflict')
      paper.state = 1; paper.submittedAt = now(); paper.submissionKind = 0; grade(paper); changed(activity.id)
      return respond(operation, session(activity, user))
    }
    if (suffix === '/sessions/{sessionId}/answers/{itemId}') {
      const key = `${paper.id}:${user.userId}:${body.requestId}`
      const receipt = answerReceipts.get(key)
      if (receipt) return receipt.itemId === p.itemId && Array.isArray(body.optionIds) && equal(receipt.options, body.optionIds)
        ? respond(operation, session(activity, user)) : fail(409, 'Request identity was reused with different input', 'InvalidRequestIdentity')
      if (!session(activity, user).canAnswer) return fail(409, 'Answering unavailable', 'PaperClosed')
      const item = paper.items.find((item: Data) => item.id === p.itemId)
      if (!item) return fail(404, 'Question not found')
      if (body.expectedRevision !== item.answerRevision) return fail(409, 'Answer changed', 'AnswerConflict')
      if (!Array.isArray(body.optionIds) || new Set(body.optionIds).size !== body.optionIds.length || body.optionIds.some((optionId: string) => !item.options.some((option: Data) => option.id === optionId))
        || item.kind !== 1 && body.optionIds.length > 1) return fail(400, 'Invalid answer', 'AnswerInvalid')
      item.selected = [...body.optionIds]; item.answeredById = user.userId; item.answeredByName = user.userName; item.answeredAt = now()
      item.answerRevision = crypto.randomUUID(); paper.answerRevision = crypto.randomUUID()
      answerReceipts.set(key, { itemId: item.id, options: [...body.optionIds] }); changed(activity.id)
      return respond(operation, session(activity, user))
    }
    return fail(404, 'Demo route not found')
  }
    return { handle, changes, activities, sessions: papers }
}
