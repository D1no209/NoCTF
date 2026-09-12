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
  const metric = (
    kind: number,
    unit: number,
    value: number,
    status = 0,
    sampleCount: number | null = null,
    windowSeconds: number | null = sampleCount === null ? null : 300,
    minimumSamples: number | null = sampleCount === null ? null : 50,
  ) => ({
    kind,
    unit,
    value,
    status,
    sampleCount,
    minimumSamples,
    windowSeconds,
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
      metric(23, 1, 38.4, 0, null, 300),
      metric(24, 0, 11_520, 0, null, 300),
      metric(25, 1, 0.18, 0, null, 300),
      metric(26, 0, 54, 0, null, 300),
      metric(27, 4, 73.8, 0, 10_944, 300, 20),
      metric(28, 2, 342, 0, 11_520, 300, 20),
      metric(29, 4, 0.4, 0, 11_520, 300, 20),
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

function mockWriteUpPdf(teamName: string): Blob {
  const safeName = teamName.replace(/[()\\]/g, '')
  const stream = `BT /F1 20 Tf 72 740 Td (NoCTF Mock WriteUp) Tj 0 -32 Td /F1 12 Tf (Team: ${safeName}) Tj 0 -24 Td (Challenge evidence and adjudication notes.) Tj ET`
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
  ]
  let pdf = '%PDF-1.4\n'
  const offsets: number[] = [0]
  for (let index = 0; index < objects.length; index++) {
    offsets.push(new TextEncoder().encode(pdf).length)
    pdf += `${index + 1} 0 obj\n${objects[index]}\nendobj\n`
  }
  const xrefOffset = new TextEncoder().encode(pdf).length
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`
  pdf += offsets.slice(1).map(offset => `${String(offset).padStart(10, '0')} 00000 n \n`).join('')
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xrefOffset}\n%%EOF\n`
  return new Blob([pdf], { type: 'application/pdf' })
}

export function createMockApi() {
  const state = createFixtures()
  const invitationTokens = new Map(state.teams.map((team, index) => [
    team.id,
    `mock${String(index + 1).padStart(28, '0')}`,
  ]))
  const sessions = new Map<string, string>()
  const tokens = new Map<string, string>()
  const administratorIssuedTokens = new Map<string, Data>()
  const wallpapers = new Map<string, Blob>()
  const competitionPosters = new Map<string, Blob | null>()
  const teamWriteUps = new Map<string, { metadata: Data; content: Blob }>()
  const writeUpPreviewTickets = new Map<string, string>()
  const writeUpAdjustments = new Map<string, number>()
  for (const team of state.teams.filter(team => team.competitionId === state.competitions[0]?.id).slice(0, 3)) {
    const content = mockWriteUpPdf(team.name)
    teamWriteUps.set(team.id, {
      content,
      metadata: {
        teamId: team.id,
        teamName: team.name,
        fileId: crypto.randomUUID(),
        fileName: `${team.name}-writeup.pdf`,
        contentType: 'application/pdf',
        byteLength: content.size,
        sha256: team.id.replaceAll('-', '').padEnd(64, '0').slice(0, 64),
        submittedByUserId: team.captainId,
        submittedByDisplayName: state.users.find(user => user.userId === team.captainId)?.userName ?? 'Mock Player',
        submittedAt: date(-0.5),
      },
    })
  }
  const changes = new Set<(competitionId: string) => void>()
  const json = (value: any, status = 200, headers: HeadersInit = {}) => Response.json(value, { status, headers: { 'X-NoCTF-Mock': 'true', 'Cache-Control': 'no-store', ...headers } })
  const problem = (status: number, detail: string) => json({ status, title: 'Mock API', detail }, status)
  const defaultHumanVerification = () => ({
    enabled: false, runtimeEnabled: true, provider: 'None', ready: false,
    capServerUrl: '', capSiteKey: '', capSecretConfigured: false,
    turnstileSiteKey: '', turnstileSecretConfigured: false,
    turnstileAllowedHostnames: [], updatedAt: now(),
  })
  const currentHumanVerification = () =>
    state.settings.get('platform/human-verification') ?? defaultHumanVerification()
  function humanVerificationReady(configuration: Data) {
    return configuration.provider === 'Cap'
      ? Boolean(configuration.capServerUrl && configuration.capSiteKey && configuration.capSecretConfigured)
      : configuration.provider === 'Turnstile'
        ? Boolean(configuration.turnstileSiteKey && configuration.turnstileSecretConfigured
          && configuration.turnstileAllowedHostnames?.length)
        : false
  }
  function syncPublicHumanVerification(configuration: Data) {
    const ready = humanVerificationReady(configuration)
    configuration.ready = ready
    state.platform.humanVerification = !configuration.enabled || !ready
      ? { provider: 'None', siteKey: null, apiEndpoint: null, runtimeRequired: false }
      : configuration.provider === 'Cap'
        ? { provider: 'Cap', siteKey: configuration.capSiteKey, apiEndpoint: `${configuration.capServerUrl.replace(/\/$/, '')}/${encodeURIComponent(configuration.capSiteKey)}/`, runtimeRequired: configuration.runtimeEnabled !== false }
        : { provider: 'Turnstile', siteKey: configuration.turnstileSiteKey, apiEndpoint: null, runtimeRequired: configuration.runtimeEnabled !== false }
  }
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
    const bearerToken = request.headers.get('Authorization')?.replace(/^Bearer /, '') ?? ''
    const impersonatedRequest = [...administratorIssuedTokens.values()]
      .some(item => item.accessToken === bearerToken)
    if (impersonatedRequest && route.startsWith('/admin/platform/users/')
      && route.includes('/tokens')) return problem(403, '模拟身份不能管理 JWT / Impersonated sessions cannot manage JWTs')

    const competition = state.competitions.find(c => c.id === p.competitionId)
    if (p.competitionId && !competition) return problem(404, '演示比赛不存在 / Competition not found')
    const competitionStaff = user?.role === 'Administrator' || user?.role === 'Organizer'
    if (competition?.accessMode === 'StaffOnly'
      && !competitionStaff
      && route !== '/writeup-previews/{competitionId}/{teamId}')
      return problem(404, '演示比赛不存在 / Competition not found')
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
      if (route === '/competitions/{competitionId}/teams/me/writeup') {
        if (!user) return problem(401, '请先登录演示账号 / Sign in to the Mock site')
        const writeUp = myTeam ? teamWriteUps.get(myTeam.id) : null
        return writeUp ? json(writeUp.metadata) : problem(404, '尚未提交题解 / No WriteUp submitted')
      }
      if (route === '/competitions/{competitionId}/teams/me/writeup/content') {
        if (!user) return problem(401, '请先登录演示账号 / Sign in to the Mock site')
        const writeUp = myTeam ? teamWriteUps.get(myTeam.id) : null
        return writeUp
          ? new Response(writeUp.content, { headers: { 'Content-Type': 'application/pdf', 'Content-Disposition': `attachment; filename="${writeUp.metadata.fileName}"`, 'Cache-Control': 'private,no-store', 'Content-Security-Policy': "sandbox; default-src 'none'", 'Cross-Origin-Resource-Policy': 'same-origin', 'X-Content-Type-Options': 'nosniff', 'X-NoCTF-Mock': 'true' } })
          : problem(404, '尚未提交题解 / No WriteUp submitted')
      }
      if (route === '/competitions/{competitionId}/teams/{teamId}/writeup/content') {
        if (!competitionStaff) return problem(403, '需要竞赛工作人员权限 / Staff access required')
        const writeUp = teamWriteUps.get(p.teamId!)
        return writeUp
          ? new Response(writeUp.content, { headers: { 'Content-Type': 'application/pdf', 'Content-Disposition': `attachment; filename="${writeUp.metadata.fileName}"`, 'Cache-Control': 'private,no-store', 'Content-Security-Policy': "sandbox; default-src 'none'", 'Cross-Origin-Resource-Policy': 'same-origin', 'X-Content-Type-Options': 'nosniff', 'X-NoCTF-Mock': 'true' } })
          : problem(404, '尚未提交题解 / No WriteUp submitted')
      }
      if (route === '/writeup-previews/{competitionId}/{teamId}') {
        const writeUp = teamWriteUps.get(p.teamId!)
        const ticket = request.headers.get('cookie')
          ?.match(/(?:^|;\s*)noctf_writeup_preview=([^;]+)/)?.[1]
        return writeUp && ticket === writeUpPreviewTickets.get(p.teamId!)
          ? new Response(writeUp.content, { headers: { 'Content-Type': 'application/pdf', 'Cache-Control': 'private,no-store', 'Accept-Ranges': 'bytes', 'Cross-Origin-Resource-Policy': 'same-origin', 'X-Content-Type-Options': 'nosniff', 'X-NoCTF-Mock': 'true' } })
          : problem(401, '预览授权已失效 / Preview grant expired')
      }
      if (route === '/competitions/{competitionId}/writeups') {
        if (!competitionStaff) return problem(403, '需要竞赛工作人员权限 / Staff access required')
        const writeUps = state.teams
          .filter(team => team.competitionId === p.competitionId)
          .map(team => ({ team, writeUp: teamWriteUps.get(team.id) }))
          .filter(item => item.writeUp)
          .map(({ team, writeUp }) => ({
            writeUp: writeUp!.metadata,
            totalScore: state.challenges
              .filter(challenge => challenge.competitionId === p.competitionId)
              .reduce((total, challenge) => total + (state.facts.some(fact => fact.teamId === team.id && fact.competitionChallengeId === challenge.id && fact.result === 'Correct') ? 500 : 0)
                + (writeUpAdjustments.get(`${team.id}:${challenge.id}`) ?? 0), 0),
            challengeScores: state.challenges
              .filter(challenge => challenge.competitionId === p.competitionId)
              .map(challenge => ({
                competitionChallengeId: challenge.id,
                title: challenge.title,
                direction: challenge.direction,
                netPoints: (state.facts.some(fact => fact.teamId === team.id && fact.competitionChallengeId === challenge.id && fact.result === 'Correct') ? 500 : 0)
                  + (writeUpAdjustments.get(`${team.id}:${challenge.id}`) ?? 0),
              })),
          }))
        value = { scoreboardAvailable: true, canJudge: user?.role !== 'User', items: writeUps }
      }
      else if (route === '/auth/me/wallpaper') {
        if (!user?.wallpaperRevision) return problem(404, '尚未上传壁纸 / No wallpaper uploaded')
        const wallpaper = wallpapers.get(user.userId)
          ?? Bun.file(new URL('./data/competition-poster.png', import.meta.url))
        return new Response(wallpaper, {
          headers: { 'Content-Type': wallpaper.type || 'image/png', 'Cache-Control': 'no-store', 'X-NoCTF-Mock': 'true' },
        })
      }
      if (route === '/competitions/{competitionId}/poster') {
        const storedPoster = competitionPosters.get(p.competitionId!)
        if (storedPoster === null) return problem(404, '演示海报不存在 / No poster')
        const poster = storedPoster ?? Bun.file(new URL('./data/competition-poster.png', import.meta.url))
        const currentRevision = competition?.posterUrl
          ? new URL(competition.posterUrl, url).searchParams.get('revision')
          : null
        return new Response(poster, {
          headers: {
            'Content-Type': poster.type || 'image/png',
            'Cache-Control': competition?.accessMode === 'StaffOnly'
              ? 'private,no-store'
              : currentRevision && url.searchParams.get('revision') === currentRevision
              ? 'public,max-age=31536000,immutable'
              : 'no-store',
            'X-NoCTF-Mock': 'true',
          },
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
        humanVerification: currentHumanVerification(),
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
      else if (route === '/admin/platform/users/{userId}/tokens') {
        value = {
          items: [...administratorIssuedTokens.values()].filter(item =>
            item.issuedByUserId === user!.userId
            && item.targetUserId === p.userId
            && !item.revoked
            && Date.parse(item.expiresAt) > Date.now()),
        }
      }
      else if (route === '/admin/platform/information') value = { version: 'MOCK / local-memory', contributors: [] }
      else if (route === '/admin/platform/monitoring') value = mockMonitoringSnapshot()
      else if (route === '/admin/competitions/{competitionId}') value = {
        competition: { ...competition, administrationRole: user?.role === 'Administrator' ? 'Owner' : user?.role === 'Organizer' ? 'Manager' : null },
        modeConfiguration: { competitionId: competition!.id, mode: competition!.mode, competitionStatus: competition!.status, json: '{}', updatedAt: now() },
        tracks: state.settings.get(`/competitions/{competitionId}/tracks${p.competitionId}`) ?? { mode: competition!.mode, enabled: competition!.tracksEnabled ?? false, canUpdate: competition!.status !== 'Finished', items: [] },
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
      else if (cleanRoute === '/competitions') {
        const visibleCompetitions = route.startsWith('/admin') || competitionStaff
          ? state.competitions
          : state.competitions.filter(candidate => candidate.accessMode !== 'StaffOnly')
        value = list(visibleCompetitions.map(c => ({ ...c, administrationRole: user?.role === 'Administrator' ? 'Owner' : user?.role === 'Organizer' ? 'Manager' : null })), url)
      }
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
      else if (cleanRoute.endsWith('/invitation-token')) {
        if (!user) return problem(401, '请先登录演示账号 / Sign in to view the invitation token')
        if (!team || (team.captainId !== user.userId && !['Administrator', 'Organizer'].includes(user.role))) return problem(403, '仅队长或竞赛管理者可查看 / Captain or competition manager required')
        value = { invitationToken: invitationTokens.get(team.id) }
      }
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
        const fileId = crypto.randomUUID()
        const posterUrl = `/api/v1/competitions/${p.competitionId}/poster?revision=${fileId.replaceAll('-', '')}`
        competition!.posterUrl = posterUrl
        value = { fileId, contentType: file.type, url: posterUrl }
      }
      else if (route === '/competitions/{competitionId}/teams/me/writeup' && request.method === 'PUT') {
        if (!myTeam || myTeam.registrationStatus !== 'Approved' || myTeam.isBanned)
          return problem(403, '需要已审核且未封禁的队伍 / An approved, active team is required')
        if (competition?.writeUpSubmissionDeadlineAt
          && Date.now() > Date.parse(competition.writeUpSubmissionDeadlineAt))
          return json({ code: 'WriteUpSubmissionDeadlinePassed', detail: '题解提交期限已结束 / WriteUp submission has closed' }, 409)
        const form = await request.formData()
        const file = form.get('file')
        if (!(file instanceof File)
          || file.type !== 'application/pdf'
          || !file.name.toLowerCase().endsWith('.pdf')
          || new TextDecoder('ascii').decode(new Uint8Array(await file.slice(0, 5).arrayBuffer())) !== '%PDF-')
          return problem(422, '请选择有效 PDF / Choose a valid PDF')
        const metadata = {
          teamId: myTeam.id,
          teamName: myTeam.name,
          fileId: crypto.randomUUID(),
          fileName: file.name,
          contentType: 'application/pdf',
          byteLength: file.size,
          sha256: crypto.randomUUID().replaceAll('-', '').padEnd(64, '0').slice(0, 64),
          submittedByUserId: user!.userId,
          submittedByDisplayName: user!.userName,
          submittedAt: now(),
        }
        teamWriteUps.set(myTeam.id, { metadata, content: file })
        writeUpPreviewTickets.delete(myTeam.id)
        value = metadata
      }
      else if (route === '/competitions/{competitionId}/teams/{teamId}/writeup/preview' && request.method === 'POST') {
        if (!competitionStaff) return problem(403, '需要竞赛工作人员权限 / Staff access required')
        const writeUp = teamWriteUps.get(p.teamId!)
        if (!writeUp) return problem(404, '尚未提交题解 / No WriteUp submitted')
        const previewUrl = `/api/v1/writeup-previews/${p.competitionId}/${p.teamId}`
        const ticket = crypto.randomUUID()
        writeUpPreviewTickets.set(p.teamId!, ticket)
        return json(
          { previewUrl, expiresAt: date(10 / 60) },
          200,
          { 'Set-Cookie': `noctf_writeup_preview=${ticket}; Path=${previewUrl}; Max-Age=600; HttpOnly; SameSite=Strict` },
        )
      }
      else if (route === '/admin/competitions/{competitionId}/poster' && request.method === 'DELETE') {
        competitionPosters.set(p.competitionId!, null)
        competition!.posterUrl = null
        return new Response(null, { status: 204, headers: { 'X-NoCTF-Mock': 'true' } })
      }
      else if (route === '/admin/platform/configuration' && request.method === 'PATCH') {
        if (body.branding) Object.assign(state.platform, body.branding, { updatedAt: now() })
        if (body.humanVerification) {
          const current = currentHumanVerification()
          const updated = { ...current, ...body.humanVerification, updatedAt: now() }
          if (updated.enabled && !humanVerificationReady(updated))
            return problem(400, '请先完成所选 Provider 的配置 / Complete the selected provider configuration')
          syncPublicHumanVerification(updated)
          state.settings.set('platform/human-verification', updated)
        }
        if (body.emailVerification) state.settings.set('platform/email', body.emailVerification)
        if (body.publicGateway) state.settings.set('platform/gateway', {
          policy: body.publicGateway,
          capability: { connectorId: body.publicGateway.connectorId, runnerId: 'mock-runner', approvedOrigins: [body.publicGateway.publicOrigin], firstPort: 32768, lastPort: 60999, reservedPorts: [], maximumPorts: 8, namespaceIsolationAvailable: true },
        })
        value = {
          branding: state.platform,
          humanVerification: currentHumanVerification(),
          emailVerification: state.settings.get('platform/email'),
          publicGateway: state.settings.get('platform/gateway'),
          publicGatewayStatusUrl: '/api/v1/admin/platform/public-gateway/status',
        }
      }
      else if (route === '/admin/platform/human-verification/secret' && request.method === 'PUT') {
        if (!['Cap', 'Turnstile'].includes(body.provider) || !body.secret)
          return problem(400, 'Provider 与密钥必填 / Provider and secret are required')
        const updated = { ...currentHumanVerification(), updatedAt: now() }
        if (body.provider === 'Cap') updated.capSecretConfigured = true
        else updated.turnstileSecretConfigured = true
        syncPublicHumanVerification(updated)
        state.settings.set('platform/human-verification', updated)
        value = updated
      }
      else if (route === '/admin/platform/users/{userId}/tokens' && request.method === 'POST') {
        const target = state.users.find(candidate => candidate.userId === p.userId)
        if (!target) return problem(404, '演示账号不存在 / Mock user not found')
        if (target.accountStatus && target.accountStatus !== 'Active')
          return json({ code: 'AccountInactive', message: 'Only active accounts can receive tokens.' }, 409)
        const expiresInSeconds = Number(body.expiresInSeconds)
        const reason = typeof body.reason === 'string' ? body.reason.trim() : ''
        if (!Number.isInteger(expiresInSeconds)
          || expiresInSeconds < 60
          || expiresInSeconds > 31_536_000
          || reason.length < 3
          || reason.length > 500) return problem(400, 'JWT lifetime or reason is invalid.')
        const jwtId = crypto.randomUUID()
        const expiresAt = new Date(Date.now() + expiresInSeconds * 1000).toISOString()
        const accessToken = `mock.${Buffer.from(JSON.stringify({
          sub: target.userId,
          exp: Math.floor(Date.parse(expiresAt) / 1000),
          jti: jwtId.replaceAll('-', ''),
          impersonation: true,
          impersonator_id: user!.userId,
        })).toString('base64url')}.${crypto.randomUUID()}`
        tokens.set(accessToken, target.userId)
        administratorIssuedTokens.set(jwtId, {
          jwtId,
          targetUserId: target.userId,
          targetUserName: target.userName,
          issuedByUserId: user!.userId,
          issuedAt: now(),
          expiresAt,
          reason,
          accessToken,
          revoked: false,
        })
        value = { accessToken, expiresAt, jwtId, targetUserId: target.userId, targetUserName: target.userName }
      }
      else if (route === '/admin/platform/users/{userId}/tokens/{jwtId}' && request.method === 'DELETE') {
        const issued = administratorIssuedTokens.get(p.jwtId!)
        if (!issued || issued.issuedByUserId !== user!.userId || issued.targetUserId !== p.userId)
          return problem(404, '签发记录不存在 / Issued token not found')
        issued.revoked = true
        tokens.delete(issued.accessToken)
        return new Response(null, { status: 204, headers: { 'X-NoCTF-Mock': 'true' } })
      }
      else if (route === '/admin/platform/users/{userId}/tokens' && request.method === 'DELETE') {
        for (const [jwtId, issued] of administratorIssuedTokens) {
          if (issued.targetUserId !== p.userId) continue
          issued.revoked = true
          tokens.delete(issued.accessToken)
          administratorIssuedTokens.set(jwtId, issued)
        }
        const target = state.users.find(candidate => candidate.userId === p.userId)
        if (!target) return problem(404, '演示账号不存在 / Mock user not found')
        target.tokenVersion = Number(target.tokenVersion ?? 0) + 1
        value = { ...target, id: target.userId, accountStatus: target.accountStatus ?? 'Active', updatedAt: now() }
      }
      else if (route === '/admin/competitions' && request.method === 'POST') {
        value = { ...state.competitions[0], ...body, id: body.id ?? crypto.randomUUID(), ownerId: user!.userId, status: 'Draft' }; state.competitions.push(value)
      }
      else if (route === '/admin/competitions/{competitionId}' && request.method === 'PATCH') {
        if (body.metadata) Object.assign(competition!, body.metadata)
        if (body.modeConfiguration) state.settings.set(`${route}/configuration`, body.modeConfiguration)
        if (body.tracks) {
          competition!.tracksEnabled = body.tracks.enabled
          state.settings.set(`/competitions/{competitionId}/tracks${p.competitionId}`, { mode: competition!.mode, enabled: body.tracks.enabled, canUpdate: competition!.status !== 'Finished', items: body.tracks.tracks })
        }
        value = { competition, modeConfiguration: body.modeConfiguration ?? { json: '{}' }, tracks: state.settings.get(`/competitions/{competitionId}/tracks${p.competitionId}`) ?? { enabled: competition!.tracksEnabled ?? false, canUpdate: competition!.status !== 'Finished', items: [] }, permissions: body.permissions ?? null, leaderboardVisibility: body.leaderboardVisibility ?? { effectiveVisibility: 'Normal' }, capabilities: { canObserve: true, canModerate: true, canManagePermissions: true } }
      }
      else if (route === '/admin/competitions/{competitionId}/status' && request.method === 'PUT') { competition!.status = body.status; value = {} }
      else if (route === '/admin/competitions/{competitionId}/gameplay-facts/manual-adjustments') {
        const adjustmentKey = `${body.teamId}:${body.competitionChallengeId}`
        writeUpAdjustments.set(
          adjustmentKey,
          (writeUpAdjustments.get(adjustmentKey) ?? 0) + Number(body.delta ?? 0),
        )
        const gameplayFactId = crypto.randomUUID()
        value = {
          gameplayFactId,
          state: 'Completed',
          statusUrl: `/api/v1/competitions/${p.competitionId}/gameplay-facts/${gameplayFactId}`,
        }
      }
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
        value = { ...state.teams[0], ...body, id: body.id ?? crypto.randomUUID(), competitionId: p.competitionId, captainId: user!.userId, memberIds: [user!.userId], registeredAt: now() }; state.teams.push(value); invitationTokens.set(value.id, crypto.randomUUID().replaceAll('-', ''))
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
      else if (cleanRoute.endsWith('/invitation-token/rotate')) {
        if (!team || (team.captainId !== user!.userId && !['Administrator', 'Organizer'].includes(user!.role))) return problem(403, '仅队长或竞赛管理者可轮换 / Captain or competition manager required')
        const invitationToken = crypto.randomUUID().replaceAll('-', '')
        invitationTokens.set(team.id, invitationToken)
        value = { invitationToken }
      }
      else if (cleanRoute.endsWith('/teams/join')) {
        const invitedTeam = state.teams.find(candidate => candidate.competitionId === p.competitionId && invitationTokens.get(candidate.id) === body.invitationToken)
        if (!invitedTeam) return problem(400, '无效的演示邀请码 / Invalid demo invitation')
        if (myTeam) return problem(409, '已加入队伍 / Already in a team')
        value = invitedTeam; value.memberIds.push(user!.userId)
      }
      else if (route.endsWith('/flag-submissions')) {
        if (!myTeam) return problem(409, '先加入演示队伍 / Join a team first')
        const correct = (body.flag ?? body.flags?.[0]) === 'flag{mock_success}'
        const duplicate = competition!.mode !== 'Ctf'
          && correct
          && state.facts.some(f => f.teamId === myTeam.id && f.competitionChallengeId === challenge!.id && f.result === 'Correct')
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
      else if (route === '/competitions/{competitionId}/teams/{teamId}/writeup/consultations') {
        if (!competitionStaff || !team || !teamWriteUps.has(team.id))
          return problem(404, '题解不存在或不可访问 / WriteUp unavailable')
        const rootId = crypto.randomUUID()
        const actorRole = user!.role === 'Administrator' ? 'PlatformAdministrator' : 'CompetitionManager'
        const question = model('QuestionsCompetitionQuestionResponse', {
          ...body,
          threadRootId: rootId,
          competitionId: p.competitionId,
          teamId: team.id,
          askedByUserId: user!.userId,
          askerDisplayName: user!.userName,
          teamDisplayName: team.name,
          createdAt: now(),
          updatedAt: now(),
          status: 'Replied',
          maxParticipantMessagesBeforeHandlerReply: 5,
          challengeTitle: state.challenges.find(candidate => candidate.id === body.competitionChallengeId)?.title ?? null,
          lastActorDisplayName: user!.userName,
          lastActorRole: actorRole,
          entries: [model('QuestionsCompetitionQuestionEntryResponse', {
            id: rootId,
            kind: 'Message',
            actorRole,
            actorUserId: user!.userId,
            actorDisplayName: user!.userName,
            body: body.body,
            createdAt: now(),
          })],
        })
        state.questions.unshift(question)
        value = questionView(question, user, myTeam)
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
