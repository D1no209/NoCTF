import { accounts, createFixtures } from './data/fixtures'
import { date, id, matchOperation, model, now, resolve, responseSchema, sample, type Data } from './schema'
import { leaderboardRead } from './leaderboard'
import { questionActorRole, questionView } from './questions'
import { mockAttachmentResponse } from './attachments'
import { mockRuntimeEndpoint } from './runtime'

function shape(schema: Data | undefined, value: any): any {
  if (!schema) return value
  if (value == null) return schema.nullable ? null : sample(schema)
  const type = resolve(schema)
  if (type.oneOf) return shape(type.oneOf[0], value)
  if (type.type === 'array') return Array.isArray(value) ? value.map(item => shape(type.items, item)) : []
  if (type.properties) return Object.fromEntries(Object.entries(type.properties).map(([key, field]) => [key, shape(field as Data, value[key] ?? sample(field as Data))]))
  return value
}

function mockMonitoringSnapshot(): Data {
  const metric = (kind: number, unit: number, value: number, status = 0, sampleCount: number | null = null) => ({
    kind,
    unit,
    value,
    status,
    sampleCount,
    minimumSamples: sampleCount === null ? null : 50,
    windowSeconds: sampleCount === null ? null : 300,
  })
  const latency = (kind: number, endpoint: string, p95: number, p99: number, mean: number, rate: number, errors: number, samples: number, status = 0) => ({
    kind,
    endpoint,
    p95Milliseconds: p95,
    p99Milliseconds: p99,
    meanMilliseconds: mean,
    requestsPerSecond: rate,
    errorPercent: errors,
    sampleCount: samples,
    minimumSamples: 50,
    windowSeconds: 300,
    status,
  })
  const pool = (name: string, resource: number, available: number, total: number, onlineRunners: number) => ({
    pool: name,
    resource,
    available,
    total,
    onlineRunners,
  })

  return {
    status: 1,
    prometheusAvailable: true,
    natsAvailable: true,
    capturedAt: now(),
    dashboardUrl: null,
    latencySustainedWindowMinutes: 3,
    metrics: [
      metric(0, 1, 148.6, 0, 44_580),
      metric(1, 2, 186, 0, 44_580),
      metric(2, 4, 0.36, 0, 44_580),
      metric(3, 0, 324),
      metric(4, 0, 1),
      metric(5, 4, 72.4, 1),
      metric(6, 0, 12),
      metric(7, 0, 5),
      metric(8, 0, 1, 1),
      metric(9, 0, 24),
      metric(10, 0, 8),
      metric(11, 0, 4, 1),
      metric(12, 3, 18, 1),
      metric(13, 1, 0, 0, 300),
      metric(14, 1, 0.02, 1, 300),
      metric(15, 2, 240, 0, 300),
      metric(16, 1, 0, 0, 300),
      metric(17, 1, 0, 0, 300),
      metric(18, 0, 6),
      metric(19, 4, 23, 1),
      metric(20, 4, 68, 1),
      metric(21, 2, 44, 0, 1_820),
      metric(22, 4, 31, 1),
    ],
    latencyDetails: [
      latency(0, '/api/v1/competitions', 186, 342, 104, 88.4, 0.18, 26_520),
      latency(1, '/hubs/v1/competitions', 41_200, 58_900, 17_800, 1.08, 0.04, 324),
      latency(2, '/api/v1/auth/me/wallpaper', 580, 1_250, 312, 0.42, 1.2, 126, 1),
      latency(3, '/api/v1/files/{fileId}', 210, 460, 128, 3.9, 0.08, 1_170),
      latency(4, 'leaderboard:publish', 44, 86, 22, 6.07, 0.02, 1_820),
    ],
    poolResources: [
      pool('default', 0, 18_790_481_920, 68_719_476_736, 4),
      pool('default', 1, 3_200_000_000, 12_000_000_000, 4),
      pool('default', 2, 1_536, 4_096, 4),
      pool('burst', 0, 4_294_967_296, 17_179_869_184, 2),
      pool('burst', 1, 1_150_000_000, 4_000_000_000, 2),
      pool('burst', 2, 420, 2_048, 2),
    ],
  }
}

export function createMockApi() {
  const state = createFixtures()
  const sessions = new Map<string, string>()
  const tokens = new Map<string, string>()
  const wallpapers = new Map<string, Blob>()
  const competitionPosters = new Map<string, Blob>()
  const changes = new Set<(competitionId: string) => void>()
  const json = (value: any, status = 200, headers: HeadersInit = {}) => Response.json(value, { status, headers: { 'X-NoCTF-Mock': 'true', 'Cache-Control': 'no-store', ...headers } })
  const problem = (status: number, detail: string) => json({ status, title: 'Mock API', detail }, status)
  const userFor = (request: Request) => {
    const bearer = request.headers.get('Authorization')?.replace(/^Bearer /, '')
    return state.users.find(user => user.userId === tokens.get(bearer ?? ''))
  }
  function signIn(user: Data) {
    const session = crypto.randomUUID()
    sessions.set(session, user.userId)
    const accessToken = `mock.${Buffer.from(JSON.stringify({ sub: user.userId, exp: Math.floor(Date.now() / 1000) + 86400 })).toString('base64url')}.${crypto.randomUUID()}`
    tokens.set(accessToken, user.userId)
    return json({ accessToken }, 200, { 'Set-Cookie': `noctf_mock_session=${session}; Path=/; HttpOnly; SameSite=Strict` })
  }
  function list(items: Data[], url: URL) {
    const search = (url.searchParams.get('search') ?? url.searchParams.get('query') ?? '').toLowerCase()
    const mode = url.searchParams.get('mode')
    const status = url.searchParams.get('status')
    let filtered = items.filter(item => !item.deletedAt && (!search || JSON.stringify(item).toLowerCase().includes(search))
      && (!mode || item.mode === mode) && (!status || item.status === status))
    for (const key of ['competitionChallengeId', 'teamId', 'actorUserId', 'kind', 'state', 'result', 'trackKey', 'role']) {
      const selected = url.searchParams.get(key)
      if (selected) filtered = filtered.filter(item => String(item[key]) === selected)
    }
    const schema = resolve(responseSchema(matchOperation(url.pathname, 'GET')?.operation ?? {}))
    const paginated = 'nextCursor' in (schema.properties ?? {})
    const limit = paginated ? Math.max(1, Math.min(100, Number(url.searchParams.get('limit') ?? url.searchParams.get('pageSize') ?? 30) || 30)) : Math.max(1, filtered.length)
    const cursor = url.searchParams.get('cursor')
    const offset = paginated && cursor?.startsWith('mock:') ? Number(cursor.slice(5)) || 0 : 0
    const nextCursor = offset + limit < filtered.length ? `mock:${offset + limit}` : null
    filtered = filtered.slice(offset, offset + limit)
    return { items: filtered, nextCursor }
  }

  async function handle(request: Request): Promise<Response> {
    const url = new URL(request.url)
    const path = url.pathname
    if (path === '/health') return json({ status: 'Healthy', environment: 'Mock' })
    if (path === '/__mock/status') return json({ environment: 'Mock', users: state.users.length, competitions: state.competitions.length, challenges: state.challenges.length, attachments: state.attachments.length, facts: state.facts.length })
    const match = matchOperation(path, request.method)
    if (!match) return problem(404, `Mock route not found: ${request.method} ${path}`)
    const { params: p, operation } = match
    const route = match.path.replace('/api/v1', '')
    let body: Data = {}
    if (request.method !== 'GET' && request.method !== 'DELETE' && request.headers.get('Content-Type')?.includes('application/json')) {
      try { const content = await request.text(); if (content.trim()) body = JSON.parse(content) } catch { return problem(400, 'Invalid JSON / 请求内容不是有效 JSON') }
    }
    if (route === '/auth/refresh') {
      const cookie = request.headers.get('cookie')?.match(/(?:^|;\s*)noctf_mock_session=([^;]+)/)?.[1]
      if (cookie === 'guest') return problem(401, 'Mock session signed out')
      const user = state.users.find(u => u.userId === sessions.get(cookie ?? '')) ?? state.users[0]
      return user ? signIn(user) : problem(401, 'Mock session expired')
    }
    if (route === '/auth/login') {
      const account = accounts.find(a => a.login === body.login && a.password === body.password)
      return account ? signIn(state.users.find(u => u.userId === account.userId)!) : problem(401, '演示账号：admin / organizer / player，密码：Mock123!')
    }
    if (route === '/auth/logout' || route === '/auth/logout-all') {
      const token = request.headers.get('Authorization')?.replace(/^Bearer /, '') ?? ''
      const userId = tokens.get(token)
      tokens.delete(token)
      for (const [key, value] of sessions) if (value === userId) sessions.delete(key)
      return new Response(null, { status: 204, headers: { 'Set-Cookie': 'noctf_mock_session=guest; Path=/; HttpOnly; SameSite=Strict', 'X-NoCTF-Mock': 'true' } })
    }
    const user = userFor(request)
    if ((route.startsWith('/admin') || route.startsWith('/auth/me') || request.method !== 'GET') && !user) return problem(401, '请先登录演示账号 / Sign in to the Mock site')
    if (route.startsWith('/admin/platform') && user?.role !== 'Administrator') return problem(403, '需要 Mock 管理员账号 / Administrator required')
    if (route.startsWith('/admin/') && !['Administrator', 'Organizer'].includes(user?.role)) return problem(403, '需要 Mock 管理账号 / Staff account required')

    const competition = state.competitions.find(c => c.id === p.competitionId)
    if (p.competitionId && !competition) return problem(404, '演示比赛不存在 / Competition not found')
    const challenge = state.challenges.find(c => c.id === p.competitionChallengeId && c.competitionId === p.competitionId)
    if (p.competitionChallengeId && !challenge) return problem(404, '演示题目不存在 / Challenge not found')
    const template = state.templates.find(c => c.id === p.challengeId)
    if (p.challengeId && !template) return problem(404, '演示模板不存在 / Template not found')
    const attachment = state.attachments.find(item => item.id === p.attachmentId
      && (!challenge || item.challengeId === challenge.challengeId)
      && (!template || item.challengeId === template.id))
    if (p.attachmentId && !attachment) return problem(404, '演示附件不存在 / Attachment not found')
    const team = state.teams.find(t => t.id === p.teamId && t.competitionId === p.competitionId)
    const myTeam = state.teams.find(t => t.competitionId === p.competitionId && t.memberIds.includes(user?.userId))
    const cleanRoute = route.replace('/admin/competitions', '/competitions')
    let value: any
    let status = Number(Object.keys(operation.responses).find(key => /^2\d\d$/.test(key)) ?? 200)
    let changed = false

    if (request.method === 'GET') {
      if (route === '/auth/me/wallpaper') {
        if (!user?.wallpaperRevision) return problem(404, '尚未上传壁纸 / No wallpaper uploaded')
        const wallpaper = wallpapers.get(user.userId)
          ?? Bun.file(new URL('./data/competition-poster.png', import.meta.url))
        return new Response(wallpaper, {
          headers: { 'Content-Type': wallpaper.type || 'image/png', 'Cache-Control': 'no-store', 'X-NoCTF-Mock': 'true' },
        })
      }
      if (route === '/competitions/{competitionId}/poster') {
        const poster = competitionPosters.get(p.competitionId!)
          ?? Bun.file(new URL('./data/competition-poster.png', import.meta.url))
        return new Response(poster, {
          headers: { 'Content-Type': poster.type || 'image/png', 'Cache-Control': 'no-store', 'X-NoCTF-Mock': 'true' },
        })
      }
      if (route === '/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments/{attachmentId}') {
        if (!user) return problem(401, '请先登录演示账号 / Sign in to download attachments')
        return mockAttachmentResponse(attachment!)
      }
      if (route === '/competitions/{competitionId}/challenges/{competitionChallengeId}/attachment') {
        if (!user) return problem(401, '请先登录演示账号 / Sign in to download attachments')
        const assigned = state.attachments.find(item => item.challengeId === challenge!.challengeId)
        if (!assigned) return problem(404, '演示附件不存在 / Attachment not found')
        return mockAttachmentResponse(assigned)
      }
      if (route === '/platform/configuration') value = state.platform
      else if (route === '/admin/platform/configuration') value = {
        branding: state.platform,
        emailVerification: state.settings.get('platform/email') ?? {
          enabled: false, publicBaseUrl: 'http://localhost:3000', tokenLifetimeMinutes: 30,
          resendCooldownSeconds: 60, passwordResetTokenLifetimeMinutes: 30,
          passwordResetCooldownSeconds: 60, passwordResetMaxRequestsPerHour: 5,
          smtpHost: '', smtpPort: 587, smtpSecurityMode: 'StartTls', smtpUserName: '',
          smtpPasswordConfigured: false, smtpFromAddress: '', smtpFromName: '', smtpTimeoutSeconds: 15,
        },
        publicGateway: state.settings.get('platform/gateway') ?? {
          policy: { enabled: false, connectorId: 'gateway', publicOrigin: '', directOrigins: [], publicRuntimeHost: '', directRuntimeHostOverride: null, maxPublishedPorts: 8 },
          capability: { connectorId: 'gateway', runnerId: 'mock-runner', approvedOrigins: ['https://public.example.test'], firstPort: 32768, lastPort: 60999, reservedPorts: [], maximumPorts: 8, namespaceIsolationAvailable: true },
        },
        publicGatewayStatusUrl: '/api/v1/admin/platform/public-gateway/status',
      }
      else if (route === '/auth/me') value = user
      else if (route === '/auth/me/profile') {
        const identity = state.settings.get(`${user!.userId}/school-identity`) ?? { fullName: '演示用户', studentNumber: 'MOCK-2026' }
        value = { description: user!.description ?? null, schoolIdentity: identity, appearance: { wallpaperEnabled: Boolean(user!.wallpaperEnabled) }, privacy: { ipRetentionDays: 7 } }
      }
      else if (route === '/users/{userId}') value = state.users.find(u => u.userId === p.userId)
      else if (route === '/admin/platform/users/{userId}') value = { user: state.users.find(u => u.userId === p.userId), schoolIdentity: state.settings.get(`${p.userId}/school-identity`) ?? { fullName: null, studentNumber: null } }
      else if (route === '/admin/platform/users') value = list(state.users.map(u => ({ ...u, id: u.userId, accountStatus: 'Active', createdAt: date(-720) })), url)
      else if (route === '/admin/platform/information') value = { version: 'MOCK / local-memory', contributors: [] }
      else if (route === '/admin/platform/monitoring') value = mockMonitoringSnapshot()
      else if (route === '/admin/competitions/{competitionId}') value = {
        competition: { ...competition, administrationRole: user?.role === 'Administrator' ? 'Owner' : user?.role === 'Organizer' ? 'Manager' : null },
        modeConfiguration: { competitionId: competition!.id, mode: competition!.mode, competitionStatus: competition!.status, json: '{}', updatedAt: now() },
        tracks: state.settings.get(`/competitions/{competitionId}/tracks${p.competitionId}`) ?? { mode: competition!.mode, isFrozen: false, items: [] },
        permissions: { competitionId: competition!.id, ownerId: competition!.ownerId, managerIds: [], judgeIds: [], observerIds: [] },
        leaderboardVisibility: { competitionId: competition!.id, effectiveVisibility: 'Normal', frozenStartAt: null, hiddenStartAt: null },
        capabilities: { canObserve: true, canModerate: true, canManagePermissions: true },
      }
      else if (route === '/admin/competitions/{competitionId}/challenges/{competitionChallengeId}') value = {
        challenge,
        mode: competition!.mode,
        competitionStatus: competition!.status,
        rulesJson: '{}',
      }
      else if (cleanRoute === '/competitions') value = list(state.competitions.map(c => ({ ...c, administrationRole: user?.role === 'Administrator' ? 'Owner' : user?.role === 'Organizer' ? 'Manager' : null })), url)
      else if (cleanRoute === '/competitions/{competitionId}') value = { ...competition, administrationRole: user?.role === 'Administrator' ? 'Owner' : user?.role === 'Organizer' ? 'Manager' : null }
      else if (cleanRoute === '/competitions/{competitionId}/challenges') value = { ...list(state.challenges.filter(c => c.competitionId === p.competitionId), url), leaderboardVisibility: 'Normal', dataScope: 'Live' }
      else if (cleanRoute === '/competitions/{competitionId}/challenges/{competitionChallengeId}') value = {
        ...challenge,
        solvedByMyTeam: competition?.mode === 'Ctf' && Boolean(myTeam && state.facts.some(fact =>
          fact.teamId === myTeam.id
          && fact.competitionChallengeId === challenge?.id
          && fact.kind === 'FlagAttempt'
          && fact.result === 'Correct')),
      }
      else if (cleanRoute === '/competitions/{competitionId}/challenges/{competitionChallengeId}/attachments') {
        if (!user) return problem(401, '请先登录演示账号 / Sign in to list attachments')
        value = { deliveryPolicy: 'All', items: state.attachments.filter(item => item.challengeId === challenge!.challengeId && !item.deletedAt) }
      }
      else if (route === '/admin/challenges') value = list(state.templates, url)
      else if (route === '/admin/challenges/{challengeId}') value = template
      else if (route === '/admin/challenges/{challengeId}/attachments') value = { deliveryPolicy: 'All', items: state.attachments.filter(item => item.challengeId === template!.id && (!item.deletedAt || url.searchParams.get('includeDeleted') === 'true')) }
      else if (cleanRoute.endsWith('/teams/me')) {
        if (!myTeam) return problem(404, '尚未加入演示队伍 / No team')
        value = myTeam
      }
      else if (cleanRoute === '/competitions/{competitionId}/teams') value = list(state.teams.filter(t => t.competitionId === p.competitionId), url)
      else if (cleanRoute === '/competitions/{competitionId}/teams/{teamId}') value = team
      else if (cleanRoute === '/competitions/{competitionId}/tracks') value = state.settings.get(cleanRoute + p.competitionId) ?? { items: [model('TracksCompetitionTrackResponse', {
        key: 'open', name: '公开赛道 / Open', isDefault: true, isPublicSelectable: true, earnsScore: true, earnsBlood: true,
        affectsDynamicChallengeScore: true, visibleOnLeaderboard: true, affectsCompetitiveResults: true, isViewerTrack: true,
      })] }
      else if (cleanRoute.includes('/leaderboard')) value = leaderboardRead(state, cleanRoute.split('/leaderboard')[1]!, p.competitionId!)
      else if (route === '/notifications' || route === '/notifications/feed') value = { ...list(state.notifications.filter(n => !url.searchParams.get('competitionId') || n.targetId === url.searchParams.get('competitionId')), url), ...(route.endsWith('/feed') ? { nextCursor: 'mock:feed' } : {}) }
      else if (cleanRoute.endsWith('/questions')) value = list(state.questions.filter(q => q.competitionId === p.competitionId).map(q => questionView(q, user, myTeam)), url)
      else if (cleanRoute.endsWith('/questions/{threadRootId}')) {
        const question = state.questions.find(q => q.threadRootId === p.threadRootId && q.competitionId === p.competitionId)
        if (!question) return problem(404, 'Mock question not found')
        value = questionView(question, user, myTeam)
      }
      else if (cleanRoute.endsWith('/gameplay-facts')) value = list(state.facts.filter(f => f.competitionId === p.competitionId), url)
      else if (cleanRoute.endsWith('/gameplay-facts/{gameplayFactId}')) value = state.facts.find(f => f.id === p.gameplayFactId)
      else if (cleanRoute.endsWith('/gameplay-facts/{gameplayFactId}/value')) value = { gameplayFactId: p.gameplayFactId, value: state.facts.find(f => f.id === p.gameplayFactId)?.value ?? '' }
      else if (cleanRoute.endsWith('/runtimes/current') || route.endsWith('/test-runtimes/current')) {
        value = [...state.runtimes].reverse().find(r => r.competitionChallengeId === challenge?.id && r.challengeId === template?.id)
        if (!value) return problem(404, '演示实例未启动 / No active Mock runtime')
      }
      else if (route === '/admin/runtimes/{runtimeInstanceId}' || route.endsWith('/runtimes/{runtimeInstanceId}')) value = state.runtimes.find(r => r.id === p.runtimeInstanceId)
      else if (route.endsWith('/runtimes')) value = list(state.runtimes.filter(r => !p.competitionId || r.competitionId === p.competitionId).map(r => route.startsWith('/admin/platform') ? { runtime: r, scope: 'Competition', competitionTitle: competition?.title ?? state.competitions[0]!.title, challengeTitle: 'Mock runtime' } : r), url)
      else if (route.endsWith('/permissions') && competition) value = { competitionId: competition.id, ownerId: competition.ownerId, managerIds: [id(1, 2)], judgeIds: [], observerIds: [] }
      else if (route.endsWith('/permission-candidates')) value = { items: state.users }
      else if (route.endsWith('/configuration') && competition) value = { competitionId: competition.id, mode: competition.mode, competitionStatus: competition.status, json: JSON.stringify({ schemaVersion: { Ctf: 2, Awd: 2, Awdp: 4, Koh: 1 }[competition.mode as string] }), updatedAt: now() }
      else if (/\/(avatar|poster|logo|attachment)$/.test(route)) return new Response(null, { status: 404 })
      const saved = state.settings.get(path)
      if (saved) value = saved
      // Known secondary GET screens get their exact schema with empty collections.
      value ??= sample(responseSchema(operation))
    }
    else {
      if (route === '/auth/me/profile' && request.method === 'PATCH') {
        if (body.profile) user!.description = body.profile.description ?? null
        if (body.schoolIdentity) state.settings.set(`${user!.userId}/school-identity`, body.schoolIdentity)
        if (body.appearance) {
          if (body.appearance.wallpaperEnabled && !user!.wallpaperRevision)
            return json({ code: 'WallpaperNotUploaded' }, 400)
          user!.wallpaperEnabled = Boolean(body.appearance.wallpaperEnabled)
        }
        const identity = state.settings.get(`${user!.userId}/school-identity`) ?? { fullName: null, studentNumber: null }
        value = { description: user!.description ?? null, schoolIdentity: identity, appearance: { wallpaperEnabled: Boolean(user!.wallpaperEnabled) }, privacy: { ipRetentionDays: 7 } }
      }
      else if (route === '/auth/me/wallpaper' && request.method === 'PUT') {
        const form = await request.formData()
        const file = form.get('file')
        if (!(file instanceof Blob) || !file.type.startsWith('image/'))
          return json({ code: 'UnsupportedFormat' }, 400)
        wallpapers.set(user!.userId, file)
        Object.assign(user!, { wallpaperRevision: crypto.randomUUID(), wallpaperEnabled: true })
        value = user
      }
      else if (route === '/admin/competitions/{competitionId}/poster' && request.method === 'PUT') {
        const form = await request.formData()
        const file = form.get('file')
        if (!(file instanceof Blob) || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type))
          return json({ code: 'UnsupportedFormat' }, 400)
        competitionPosters.set(p.competitionId!, file)
        value = { fileId: crypto.randomUUID(), contentType: file.type }
      }
      else if (route === '/admin/platform/configuration' && request.method === 'PATCH') {
        if (body.branding) Object.assign(state.platform, body.branding, { updatedAt: now() })
        if (body.emailVerification) state.settings.set('platform/email', body.emailVerification)
        if (body.publicGateway) state.settings.set('platform/gateway', {
          policy: body.publicGateway,
          capability: { connectorId: body.publicGateway.connectorId, runnerId: 'mock-runner', approvedOrigins: [body.publicGateway.publicOrigin], firstPort: 32768, lastPort: 60999, reservedPorts: [], maximumPorts: 8, namespaceIsolationAvailable: true },
        })
        value = {
          branding: state.platform,
          emailVerification: state.settings.get('platform/email'),
          publicGateway: state.settings.get('platform/gateway'),
          publicGatewayStatusUrl: '/api/v1/admin/platform/public-gateway/status',
        }
      }
      else if (route === '/admin/competitions' && request.method === 'POST') {
        value = { ...state.competitions[0], ...body, id: body.id ?? crypto.randomUUID(), ownerId: user!.userId, status: 'Draft' }; state.competitions.push(value)
      }
      else if (route === '/admin/competitions/{competitionId}' && request.method === 'PATCH') {
        if (body.metadata) Object.assign(competition!, body.metadata)
        if (body.modeConfiguration) state.settings.set(`${route}/configuration`, body.modeConfiguration)
        value = { competition, modeConfiguration: body.modeConfiguration ?? { json: '{}' }, tracks: { items: [] }, permissions: body.permissions ?? null, leaderboardVisibility: body.leaderboardVisibility ?? { effectiveVisibility: 'Normal' }, capabilities: { canObserve: true, canModerate: true, canManagePermissions: true } }
      }
      else if (route === '/admin/competitions/{competitionId}/status' && request.method === 'PUT') { competition!.status = body.status; value = {} }
      else if (route === '/admin/challenges' && request.method === 'POST') { value = { ...state.templates[0], ...body, id: body.id ?? crypto.randomUUID(), ownerId: user!.userId, createdAt: now(), updatedAt: now() }; state.templates.push(value) }
      else if (route === '/admin/challenges/{challengeId}' && request.method === 'PATCH') { if (body.content) Object.assign(template!, body.content, { updatedAt: now() }); if (body.permissions) Object.assign(template!, body.permissions); value = template; state.challenges.filter(c => c.challengeId === template!.id).forEach(c => Object.assign(c, { title: template!.title, description: template!.description, direction: template!.direction })) }
      else if (route === '/admin/competitions/{competitionId}/challenges' && request.method === 'POST') {
        const source = state.templates.find(t => t.id === body.challengeId && t.mode === competition!.mode)
        if (!source) return problem(400, '选择同赛制的演示模板 / Select a template in the same mode')
        value = { ...state.challenges[0], ...body, id: body.id ?? crypto.randomUUID(), competitionId: competition!.id, title: body.customTitle ?? source.title, description: source.description, direction: source.direction, challengeId: source.id }; state.challenges.push(value)
      }
      else if (route === '/admin/competitions/{competitionId}/challenges/{competitionChallengeId}' && request.method === 'PATCH') { if (body.presentation) Object.assign(challenge!, body.presentation, { title: body.presentation.customTitle ?? challenge!.title }); value = { challenge, mode: competition!.mode, competitionStatus: competition!.status, rulesJson: body.rules?.json ?? '{}' } }
      else if (cleanRoute === '/competitions/{competitionId}/teams' && request.method === 'POST') {
        if (myTeam) return problem(409, '已加入队伍，请先退出 / Already in a team')
        value = { ...state.teams[0], ...body, id: body.id ?? crypto.randomUUID(), competitionId: p.competitionId, captainId: user!.userId, memberIds: [user!.userId], registeredAt: now() }; state.teams.push(value)
      }
      else if (cleanRoute === '/competitions/{competitionId}/teams/{teamId}' && request.method === 'PATCH') {
        if (!team || (team.captainId !== user!.userId && user!.role !== 'Administrator')) return problem(403, '仅队长可以编辑 / Captain required')
        if (body.profile) Object.assign(team, body.profile)
        if (body.membership) Object.assign(team, body.membership)
        if (body.registration) team.registrationStatus = body.registration.status
        if (body.administration) {
          team.trackKey = body.administration.trackKey
          team.registrationStatus = body.administration.registrationStatus
        }
        if (body.ban) team.isBanned = body.ban.isBanned
        value = team
      }
      else if (cleanRoute.endsWith('/teams/me/membership') && request.method === 'DELETE') { if (myTeam) myTeam.memberIds = myTeam.memberIds.filter((member: string) => member !== user!.userId); value = {} }
      else if (cleanRoute.endsWith('/invitation-token/rotate')) value = { invitationToken: 'mock00000000000000000000000000001' }
      else if (cleanRoute.endsWith('/teams/join')) {
        if (body.invitationToken !== 'mock00000000000000000000000000001') return problem(400, '无效的演示邀请码 / Invalid demo invitation')
        if (myTeam) return problem(409, '已加入队伍 / Already in a team')
        value = state.teams.find(t => t.competitionId === p.competitionId)!; value.memberIds.push(user!.userId)
      }
      else if (route.endsWith('/flag-submissions')) {
        if (!myTeam) return problem(409, '先加入演示队伍 / Join a team first')
        const correct = (body.flag ?? body.flags?.[0]) === 'flag{mock_success}'
        const duplicate = correct && state.facts.some(f => f.teamId === myTeam.id && f.competitionChallengeId === challenge!.id && f.result === 'Correct')
        const fact = model('GameplayFactsGameplayFactListItemResponse', {
          id: crypto.randomUUID(), competitionId: p.competitionId, competitionChallengeId: challenge!.id, teamId: myTeam.id,
          actorUserId: user!.userId, kind: competition!.mode === 'Awdp' ? 'BreakAttempt' : 'FlagAttempt', state: 'Completed', result: duplicate ? 'Duplicate' : correct ? 'Correct' : 'Wrong', occurredAt: now(), updatedAt: now(), value: body.flag ?? body.flags?.[0],
        })
        fact.gameplayFactId = fact.id
        state.facts.unshift(fact)
        const accepted = { gameplayFactId: fact.id, state: 'Completed', statusUrl: `/api/v1/competitions/${p.competitionId}/gameplay-facts/${fact.id}` }
        value = { ...accepted, items: [accepted], gameplayFacts: [accepted] }
      }
      else if ((route.endsWith('/runtimes') && request.method === 'POST')
        || (route.endsWith('/runtimes/{runtimeInstanceId}') && (request.method === 'DELETE' || request.method === 'PATCH'))) {
        const action = request.method === 'POST' ? (body.replacesRuntimeId ? 'reset' : 'start') : request.method === 'DELETE' ? 'stop' : 'extend'
        let runtime = state.runtimes.find(r => r.competitionChallengeId === challenge?.id && r.challengeId === template?.id && r.state !== 'Stopped')
        if (action === 'start' || action === 'reset') {
          if (runtime) runtime.state = 'Stopped'
          const runtimeIndex = state.challenges.findIndex(item => item.id === challenge?.id)
          runtime = model('RuntimeRuntimeResponse', { id: crypto.randomUUID(), competitionId: p.competitionId, competitionChallengeId: challenge?.id,
            challengeId: template?.id, teamId: myTeam?.id ?? null, runtimeKind: 'Container', provider: 'Docker', state: 'Running', createdAt: now(), runningAt: now(), expiresAt: date(1), ...mockRuntimeEndpoint(runtimeIndex), purpose: template ? 'TemplateTest' : 'Competition', runnerId: 'mock-runner', publishedPorts: [], flagState: 'Ready', testFlag: 'flag{mock_success}' })
          state.runtimes.push(runtime)
        }
        if (!runtime) return problem(404, '演示实例未启动 / No runtime')
        if (action === 'stop') Object.assign(runtime, { state: 'Stopped', stoppedAt: now() })
        if (action === 'extend') runtime.expiresAt = date(2)
        value = { ...runtime, runtimeInstanceId: runtime.id, statusUrl: path.replace(/\/{runtimeInstanceId}$/, '/current') }
      }
      else if (route === '/competitions/{competitionId}/questions') {
        const rootId = crypto.randomUUID()
        const question = model('QuestionsCompetitionQuestionResponse', { ...body, threadRootId: rootId, competitionId: p.competitionId, teamId: myTeam?.id ?? null, askedByUserId: user!.userId, askerDisplayName: user!.userName, teamDisplayName: myTeam?.name, createdAt: now(), updatedAt: now(), status: 'Pending', maxParticipantMessagesBeforeHandlerReply: 5,
          challengeTitle: state.challenges.find(c => c.id === body.competitionChallengeId)?.title ?? null,
          lastActorDisplayName: user!.userName, lastActorRole: 'Asker',
          entries: [model('QuestionsCompetitionQuestionEntryResponse', { id: rootId, kind: 'Message', actorRole: 'Asker', actorUserId: user!.userId, actorDisplayName: user!.userName, body: body.body, createdAt: now() })] })
        state.questions.unshift(question); value = questionView(question, user, myTeam)
      }
      else if (route === '/competitions/{competitionId}/questions/{threadRootId}/messages' || route === '/competitions/{competitionId}/questions/{threadRootId}/status') {
        const question = state.questions.find(q => q.threadRootId === p.threadRootId && q.competitionId === p.competitionId)
        if (!question) return problem(404, 'Mock question not found')
        const view = questionView(question, user, myTeam)
        const actorRole = questionActorRole(user!)
        const actor = { id: crypto.randomUUID(), actorRole, actorUserId: user!.userId, actorDisplayName: user!.userName, createdAt: now() }
        if (route.endsWith('/messages')) {
          if (!view.canReply) return problem(403, 'Mock question cannot receive replies')
          if (typeof body.body !== 'string' || !body.body.trim()) return problem(400, 'Mock message body is required')
          question.entries.push(model('QuestionsCompetitionQuestionEntryResponse', { ...actor, kind: 'Message', body: body.body.trim() }))
          question.status = view.access === 'Handler' ? 'Replied' : 'Pending'
        } else {
          if (body.status === 'Resolved' ? !view.canResolve : body.status === 'Closed' ? !view.canClose : true) return problem(403, 'Mock status transition is unavailable')
          question.entries.push(model('QuestionsCompetitionQuestionEntryResponse', { ...actor, kind: 'StatusTransition', fromStatus: question.status, toStatus: body.status }))
          question.status = body.status
        }
        question.updatedAt = now(); question.lastActorRole = actorRole; question.lastActorDisplayName = user!.userName
        value = questionView(question, user, myTeam)
      }
      else if (route === '/admin/competitions/{competitionId}/announcements') {
        value = model('NotificationsNotificationResponse', { id: crypto.randomUUID(), sourceType: 2, sourceId: p.competitionId, targetType: 2, targetId: p.competitionId, kind: 'CompetitionAnnouncement', content: body, sentAt: now(), sourceDisplayName: user!.userName }); state.notifications.unshift(value)
      }
      else return problem(501, `尚未模拟此操作，未调用真实服务 / Mock operation not implemented: ${request.method} ${route}`)
      changed = true
    }
    if (changed && p.competitionId) for (const notify of changes) notify(p.competitionId)
    if (status === 204) return new Response(null, { status, headers: { 'X-NoCTF-Mock': 'true' } })
    return json(shape(responseSchema(operation, String(status)), value), status)
  }
  return { state, handle, changes, userFor }
}
