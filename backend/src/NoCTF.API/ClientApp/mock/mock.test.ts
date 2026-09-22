import { describe, expect, test } from 'bun:test'
import { createMockApi } from './api'
import { createMockRealtime } from './realtime'
import { id, operations, resolve, responseSchema, type Data } from './schema'

const base = 'http://127.0.0.1:5081'
const competition = `/api/v1/competitions/${id(2)}`

async function setup(login = 'admin') {
  const api = createMockApi()
  const response = await api.handle(new Request(base + '/api/v1/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login, password: 'Mock123!' }),
  }))
  const { accessToken } = await response.json()
  const request = (path: string, method = 'GET', body?: Data) => new Request(base + path, {
    method, headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
    ...(body ? { body: JSON.stringify(body) } : {}),
  })
  return { api, accessToken, request, send: (path: string, method = 'GET', body?: Data) => api.handle(request(path, method, body)) }
}

function validate(schema: Data | undefined, value: any, path = '$') {
  if (!schema) return
  if (value === null) { expect(schema.nullable, path).toBe(true); return }
  schema = resolve(schema)
  if (schema.enum) expect(schema.enum, path).toContain(value)
  if (schema.oneOf) return validate(schema.oneOf[0], value, path)
  if (schema.type === 'array') {
    expect(Array.isArray(value), path).toBe(true)
    value.forEach((item: any, i: number) => validate(schema!.items, item, `${path}[${i}]`))
  }
  if (schema.properties) {
    expect(typeof value, path).toBe('object')
    for (const [key, field] of Object.entries(schema.properties)) validate(field as Data, value[key], `${path}.${key}`)
    if (schema.additionalProperties === false) expect(Object.keys(value).every(key => key in schema!.properties), path).toBe(true)
  }
  if (['string', 'number', 'boolean'].includes(schema.type)) expect(typeof value, path).toBe(schema.type)
  if (schema.type === 'integer') expect(Number.isInteger(value), path).toBe(true)
}

describe('isolated Mock API', () => {
  test('seeds one downloadable challenge for every supported direction', async () => {
    const { send } = await setup()
    const catalog = await (await send(`${competition}/challenges`)).json()
    const directions = ['Misc', 'Web', 'Crypto', 'Pwn', 'Reverse', 'Penetration', 'Forensics', 'OSINT', 'AI', 'Mobile', 'IoT', 'Hardware', 'Cloud', 'Blockchain']
    expect(catalog.items).toHaveLength(directions.length)
    expect(new Set(catalog.items.map((item: Data) => item.direction))).toEqual(new Set(directions))
    expect(catalog.items.every((item: Data) => item.hasRuntime)).toBe(true)
    const sockets = new Set<string>()
    for (const challenge of catalog.items) {
      const path = `${competition}/challenges/${challenge.id}/attachments`
      const response = await send(path)
      expect(response.status, challenge.direction).toBe(200)
      const attachmentList = await response.json()
      expect(attachmentList.deliveryPolicy).toBe('All')
      expect(attachmentList.items).toHaveLength(1)
      expect(attachmentList.items[0].exactFlag).toBeNull()
      const download = await send(`${path}/${attachmentList.items[0].id}`)
      expect(download.status, challenge.direction).toBe(200)
      expect(download.headers.get('content-disposition')).toContain(attachmentList.items[0].fileName)
      expect(download.headers.get('content-security-policy')).toBe("sandbox; default-src 'none'")
      expect(download.headers.get('x-content-type-options')).toBe('nosniff')
      expect(download.headers.get('cross-origin-resource-policy')).toBe('same-origin')
      expect(download.headers.get('content-type')).toContain('text/plain')
      expect(await download.text()).toContain(`Direction: ${challenge.direction}`)
      const runtime = await (await send(`${competition}/challenges/${challenge.id}/runtimes/current`)).json()
      expect(runtime.state).toBe('Running')
      expect(runtime.accesses).toHaveLength(1)
      expect(runtime.accesses[0].directAddress).toMatch(/^tcp:\/\/challenge\.mock\.invalid:31\d{3}$/)
      sockets.add(runtime.accesses[0].directAddress)
    }
    expect(sockets.size).toBe(directions.length)
  })

  test('seeds solved CTF and distinct AWDP attack-defense progress states', async () => {
    const { send } = await setup()
    const ctf = await (await send(`${competition}/leaderboard`)).json()
    const ctfTeam = ctf.teams.find((team: Data) => team.teamId === id(5))
    expect(ctfTeam.slots[0].breakdown.find((item: Data) => item.kind === 'Solve').successfulCount).toBe(1)
    expect(Object.fromEntries(ctf.teams.flatMap((team: Data) => team.slots[0].entries
      .filter((entry: Data) => entry.award)
      .map((entry: Data) => [entry.award, team.teamId])))).toEqual({
      FirstBlood: id(5),
      SecondBlood: id(5, 2),
      ThirdBlood: id(5, 3),
    })
    for (let challengeIndex = 0; challengeIndex < ctf.teams[0].slots.length; challengeIndex++) {
      const awards = ctf.teams.flatMap((team: Data) => team.slots[challengeIndex].entries
        .map((entry: Data) => entry.award)
        .filter(Boolean))
      const expected = ['FirstBlood', 'SecondBlood', 'ThirdBlood'].slice(0, awards.length)
      expect(new Set(awards), `challenge ${challengeIndex}`).toEqual(new Set(expected))
      expect(awards.length, `challenge ${challengeIndex}`).toBe(expected.length)
    }
    expect(Object.fromEntries(ctf.teams.flatMap((team: Data) => team.slots[3].entries
      .filter((entry: Data) => entry.award)
      .map((entry: Data) => [entry.award, team.teamName])))).toEqual({
      FirstBlood: 'BlueShift',
      SecondBlood: 'ByteGarden',
      ThirdBlood: 'NullPointer',
    })

    const awdpCompetition = `/api/v1/competitions/${id(2, 3)}`
    const awdp = await (await send(`${awdpCompetition}/leaderboard`)).json()
    const awdpTeam = awdp.teams.find((team: Data) => team.teamId === id(5, 21))
    const success = (slot: Data, kind: string) => slot.breakdown.find((item: Data) => item.kind === kind).successfulCount === 1
    expect(awdpTeam.slots.slice(0, 3).map((slot: Data) => [success(slot, 'Attack'), success(slot, 'Defense')])).toEqual([
      [true, false], [false, true], [true, true],
    ])
  })

  test('question chat retains author identity, messages and status history', async () => {
    const { send } = await setup()
    const path = `${competition}/questions/${id(9)}`
    const original = await (await send(path)).json()
    expect(original.access).toBe('Handler')
    expect(original.entries.some((entry: Data) => entry.actorUserId === id(1))).toBe(true)
    expect(original.entries.some((entry: Data) => entry.actorUserId === id(1, 3))).toBe(true)
    const reply = await (await send(`${path}/messages`, 'POST', { body: '本地聊天布局验证' })).json()
    expect(reply.entries.at(-1).body).toBe('本地聊天布局验证')
    expect(reply.entries.at(-1).actorUserId).toBe(id(1))
    expect(reply.status).toBe('Replied')
    const resolved = await (await send(`${path}/status`, 'PUT', { status: 'Resolved' })).json()
    expect(resolved.entries.at(-1).toStatus).toBe('Resolved')
    expect(resolved.canReply).toBe(false)
    const closed = await (await send(`${path}/status`, 'PUT', { status: 'Closed' })).json()
    expect(closed.canClose).toBe(false)
    expect((await send(`${path}/messages`, 'POST', { body: 'closed' })).status).toBe(403)
  })

  test('optional WriteUps remain open to teams and staff during the competition', async () => {
    const { api, accessToken } = await setup('player')
    const currentCompetition = api.state.competitions.find(item => item.id === id(2))!
    currentCompetition.writeUpSubmissionRequired = false
    const form = new FormData()
    form.set('file', new File(
      ['%PDF-1.7\noptional submission\n%%EOF'],
      'optional-writeup.pdf',
      { type: 'application/pdf' },
    ))

    const submitted = await api.handle(new Request(
      base + `${competition}/teams/me/writeup`,
      {
        method: 'PUT',
        headers: { Authorization: `Bearer ${accessToken}` },
        body: form,
      },
    ))
    expect(submitted.status).toBe(200)

    const login = await api.handle(new Request(base + '/api/v1/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ login: 'admin', password: 'Mock123!' }),
    }))
    const { accessToken: staffToken } = await login.json()
    const review = await api.handle(new Request(base + `${competition}/writeups`, {
      headers: { Authorization: `Bearer ${staffToken}` },
    }))
    const payload = await review.json()
    expect(review.status).toBe(200)
    expect(payload.items.some((item: Data) =>
      item.writeUp.fileName === 'optional-writeup.pdf')).toBe(true)

    const selected = payload.items.find((item: Data) =>
      item.writeUp.fileName === 'optional-writeup.pdf')
    const issued = await api.handle(new Request(
      base + `${competition}/teams/${selected.writeUp.teamId}/writeup/preview`,
      {
        method: 'POST',
        headers: { Authorization: `Bearer ${staffToken}` },
      },
    ))
    const grant = await issued.json()
    const cookie = issued.headers.get('set-cookie')!.split(';', 1)[0]
    const preview = await api.handle(new Request(base + grant.previewUrl, {
      headers: { Cookie: cookie },
    }))
    expect(preview.status).toBe(200)
    expect(preview.headers.get('accept-ranges')).toBe('bytes')
    expect(preview.headers.has('content-security-policy')).toBe(false)
    expect(new TextDecoder('ascii').decode(
      new Uint8Array(await preview.arrayBuffer()).slice(0, 5),
    )).toBe('%PDF-')
  })

  test('WriteUp review previews PDFs adjusts challenge scores and opens consultations', async () => {
    const admin = await setup()
    const reviewPath = `${competition}/writeups`
    const initial = await (await admin.send(reviewPath)).json()
    expect(initial.items.length).toBeGreaterThan(0)
    const selected = initial.items[0]
    expect(selected.originalTotalScore).toBeNumber()
    expect(selected.originalRank).toBeNumber()
    expect(selected.adjustedTotalScore).toBeNumber()
    expect(selected.adjustedRank).toBeNumber()
    const content = await admin.send(
      `${competition}/teams/${selected.writeUp.teamId}/writeup/content`,
    )
    expect(content.status).toBe(200)
    expect(content.headers.get('content-type')).toContain('application/pdf')
    expect(new TextDecoder('ascii').decode(
      new Uint8Array(await content.arrayBuffer()).slice(0, 5),
    )).toBe('%PDF-')

    const score = selected.challengeScores.find((item: Data) => item.netPoints > 0)
      ?? selected.challengeScores[0]
    const adjusted = await admin.send(
      `/api/v1/admin/competitions/${id(2)}/gameplay-facts/manual-adjustments`,
      'POST',
      {
        teamId: selected.writeUp.teamId,
        competitionChallengeId: score.competitionChallengeId,
        delta: -Math.max(1, score.netPoints),
      },
    )
    expect(adjusted.status).toBe(202)
    const refreshed = await (await admin.send(reviewPath)).json()
    const adjustedTeam = refreshed.items.find((item: Data) =>
      item.writeUp.teamId === selected.writeUp.teamId)
    const adjustedChallenge = adjustedTeam.challengeScores.find((item: Data) =>
      item.competitionChallengeId === score.competitionChallengeId)
    expect(adjustedChallenge.netPoints).toBe(0)
    expect(adjustedTeam.adjustedTotalScore)
      .toBe(selected.adjustedTotalScore - score.netPoints)
    expect(adjustedTeam.originalTotalScore).toBe(selected.originalTotalScore)

    const consulted = await admin.send(
      `${competition}/teams/${selected.writeUp.teamId}/writeup/consultations`,
      'POST',
      {
        competitionChallengeId: score.competitionChallengeId,
        title: 'WriteUp review',
        body: 'Please clarify the evidence for this challenge.',
      },
    )
    expect(consulted.status).toBe(201)
    const thread = await consulted.json()
    expect(thread.teamId).toBe(selected.writeUp.teamId)
    expect(thread.status).toBe('Replied')
  })

  test('question Mock respects participant limits and denies closing', async () => {
    const { send } = await setup('player')
    const path = `${competition}/questions/${id(9)}`
    const detail = await (await send(path)).json()
    expect(detail.access).toBe('Asker')
    expect(detail.canClose).toBe(false)
    for (let i = 0; i < detail.participantMessagesRemaining; i++)
      expect((await send(`${path}/messages`, 'POST', { body: `Follow up ${i}` })).status).toBe(200)
    expect((await send(`${path}/messages`, 'POST', { body: 'Over quota' })).status).toBe(403)
    expect((await send(`${path}/status`, 'PUT', { status: 'Closed' })).status).toBe(403)
  })

  test('primary and secondary GET responses match the current OpenAPI shape', async () => {
    const { send } = await setup()
    let checked = 0
    for (const op of operations.filter(op => op.method === 'GET' && op.path.startsWith('/api/v1'))) {
      const values: Data = { competitionId: id(2), competitionChallengeId: id(4), challengeId: id(3), teamId: id(5), userId: id(1), threadRootId: id(9), gameplayFactId: id(8), columnIndex: '0' }
      const path = op.path.replace(/\{(\w+)\}/g, (_, key) => values[key] ?? id(99))
      const response = await send(path)
      expect(response.status, path).toBeLessThan(500)
      if (response.status === 200 && response.headers.get('Content-Type')?.includes('json')) {
        validate(responseSchema(op.operation), await response.json(), path)
        checked++
      }
    }
    expect(checked).toBeGreaterThan(70)
  })

  test('invitation tokens can be read and rotated by the team captain', async () => {
    const { send } = await setup('player')
    const path = `${competition}/teams/${id(5)}/invitation-token`

    const initialResponse = await send(path)
    const initial = await initialResponse.json()
    const rotatedResponse = await send(`${path}/rotate`, 'POST')
    const rotated = await rotatedResponse.json()
    const current = await (await send(path)).json()

    expect(initialResponse.status).toBe(200)
    expect(initial.invitationToken).toHaveLength(32)
    expect(rotatedResponse.status).toBe(200)
    expect(rotated.invitationToken).toHaveLength(32)
    expect(rotated.invitationToken).not.toBe(initial.invitationToken)
    expect(current.invitationToken).toBe(rotated.invitationToken)
  })

  test('CTF resubmissions are judged normally without double scoring', async () => {
    const { send } = await setup('player')
    const seeded = await (await send(`${competition}/challenges/${id(4)}`)).json()
    const unsolved = await (await send(`${competition}/challenges/${id(4, 2)}`)).json()
    expect(seeded.solvedByMyTeam).toBe(true)
    expect(unsolved.solvedByMyTeam).toBe(false)
    const submit = async (flag: string) => {
      const response = await send(`${competition}/challenges/${id(4, 2)}/flag-submissions`, 'POST', { flag })
      expect(response.status).toBe(202)
      const accepted = await response.json()
      const fact = await (await send(accepted.statusUrl)).json()
      expect(fact.state).toBe('Completed')
      return fact.result
    }
    expect(await submit('flag{wrong}')).toBe('Wrong')
    expect(await submit('flag{mock_success}')).toBe('Correct')
    expect((await (await send(`${competition}/challenges/${id(4, 2)}`)).json()).solvedByMyTeam).toBe(true)
    expect(await submit('flag{mock_success}')).toBe('Correct')
    const board = await (await send(`${competition}/leaderboard`)).json()
    expect(board.teams.find((team: Data) => team.teamId === id(5)).totalScore).toBe(200)
    const facts = await (await send(`${competition}/gameplay-facts`)).json()
    expect(facts.items[0].result).toBe('Correct')
  })

  test('profile and platform edits persist in memory and reset with a fresh Mock instance', async () => {
    const { send } = await setup()
    await send('/api/v1/auth/me/profile', 'PATCH', { profile: { description: 'Mock profile' } })
    expect((await (await send('/api/v1/auth/me/profile')).json()).description).toBe('Mock profile')
    await send('/api/v1/admin/platform/configuration', 'PATCH', { branding: { name: 'Mock edited', description: 'Local' } })
    const publicConfiguration = await (await send('/api/v1/platform/configuration')).json()
    expect(publicConfiguration.name).toBe('Mock edited')
    expect(publicConfiguration.imageUploadLimits).toEqual({ maximumAvatarBytes: 12 * 1024 * 1024, maximumWallpaperBytes: 16 * 1024 * 1024 })
    expect(publicConfiguration.humanVerification).toEqual({ provider: 'None', siteKey: null, apiEndpoint: null, runtimeRequired: false, evaluationRequired: false })
    const secret = await send('/api/v1/admin/platform/human-verification/secret', 'PUT', { provider: 'Cap', secret: 'mock-secret' })
    expect(secret.status).toBe(200)
    const enabled = await send('/api/v1/admin/platform/configuration', 'PATCH', { humanVerification: {
      enabled: true, runtimeEnabled: false, evaluationEnabled: false, provider: 'Cap', capServerUrl: 'https://captcha.mock.invalid', capSiteKey: 'mock-site-key',
      turnstileSiteKey: '', turnstileAllowedHostnames: [],
    } })
    expect((await enabled.json()).humanVerification).toMatchObject({ enabled: true, runtimeEnabled: false, evaluationEnabled: false, provider: 'Cap', ready: true, capSecretConfigured: true })
    expect((await (await send('/api/v1/platform/configuration')).json()).humanVerification)
      .toEqual({ provider: 'Cap', siteKey: 'mock-site-key', apiEndpoint: 'https://captcha.mock.invalid/mock-site-key/', runtimeRequired: false, evaluationRequired: false })
    const fresh = await setup()
    expect((await (await fresh.send('/api/v1/platform/configuration')).json()).name).toBe('NoCTF · MOCK')
  })

  test('wallpaper upload, protected read and preference changes share account state', async () => {
    const { api, accessToken, send } = await setup()
    const before = await (await send('/api/v1/auth/me')).json()
    expect(before.wallpaperRevision).toBeNull()
    expect(before.wallpaperEnabled).toBe(false)
    expect((await send('/api/v1/auth/me/profile', 'PATCH', { appearance: { wallpaperEnabled: true } })).status).toBe(400)

    const form = new FormData()
    form.set('file', new File(['mock wallpaper'], 'wallpaper.png', { type: 'image/png' }))
    const uploaded = await api.handle(new Request(base + '/api/v1/auth/me/wallpaper', {
      method: 'PUT',
      headers: { Authorization: `Bearer ${accessToken}` },
      body: form,
    }))
    expect(uploaded.status).toBe(200)
    const current = await uploaded.json()
    expect(current.wallpaperRevision).toBeString()
    expect(current.wallpaperEnabled).toBe(true)

    const image = await send('/api/v1/auth/me/wallpaper')
    expect(image.status).toBe(200)
    expect(image.headers.get('content-type')).toContain('image/png')
    const disabled = await (await send('/api/v1/auth/me/profile', 'PATCH', { appearance: { wallpaperEnabled: false } })).json()
    expect(disabled.appearance.wallpaperEnabled).toBe(false)
    expect((await (await send('/api/v1/auth/me')).json()).wallpaperRevision).toBe(current.wallpaperRevision)
  })

  test('filters the challenge bank by direction and keeps the direction catalog', async () => {
    const { send } = await setup()
    const response = await send('/api/v1/admin/challenges?direction=Web&offset=0&limit=200')

    expect(response.status).toBe(200)
    const result = await response.json()
    expect(result.items.length).toBeGreaterThan(0)
    expect(result.items.every((item: Data) => item.direction === 'Web')).toBe(true)
    expect(result.directions).toEqual(expect.arrayContaining(['Web', 'Crypto', 'Pwn']))
  })

  test('profile cover upload remains independent from the site wallpaper', async () => {
    const { api, accessToken, send } = await setup()
    const form = new FormData()
    form.set('file', new File(['mock profile cover'], 'profile-cover.webp', { type: 'image/webp' }))
    const uploaded = await api.handle(new Request(base + '/api/v1/auth/me/profile-cover', {
      method: 'PUT',
      headers: { Authorization: `Bearer ${accessToken}` },
      body: form,
    }))

    expect(uploaded.status).toBe(200)
    const current = await uploaded.json()
    expect(current.profileCoverUrl).toContain(`/api/v1/users/${id(1)}/profile-cover?revision=`)
    expect(current.wallpaperRevision).toBeNull()
    expect(current.wallpaperEnabled).toBe(false)

    const profile = await (await send(`/api/v1/users/${id(1)}`)).json()
    expect(profile.profileCoverUrl).toBe(current.profileCoverUrl)
    const image = await send('/api/v1/users/{userId}/profile-cover'.replace('{userId}', id(1)))
    expect(image.status).toBe(200)
    expect(image.headers.get('content-type')).toContain('image/webp')
  })

  test('competition poster upload is available to the create-competition flow', async () => {
    const { api, accessToken, send } = await setup()
    const form = new FormData()
    form.set('file', new File(['new poster'], 'poster.webp', { type: 'image/webp' }))
    const uploaded = await api.handle(new Request(base + `/api/v1/admin/competitions/${id(2)}/poster`, {
      method: 'PUT',
      headers: { Authorization: `Bearer ${accessToken}` },
      body: form,
    }))
    expect(uploaded.status).toBe(200)
    const uploadedPayload = await uploaded.json()
    expect(uploadedPayload.contentType).toBe('image/webp')
    expect(uploadedPayload.url).toContain(`/api/v1/competitions/${id(2)}/poster?revision=`)

    const poster = await send(uploadedPayload.url)
    expect(poster.status).toBe(200)
    expect(poster.headers.get('content-type')).toContain('image/webp')
    expect(poster.headers.get('cache-control')).toContain('immutable')
    expect(await poster.text()).toBe('new poster')
    expect((await send(`/api/v1/admin/competitions/${id(2)}/poster`, 'DELETE')).status).toBe(204)
    const competition = (await (await send(`/api/v1/admin/competitions/${id(2)}`)).json()).competition
    expect(competition.posterUrl).toBeNull()
    expect((await send(`/api/v1/competitions/${id(2)}/poster`)).status).toBe(404)
  })

  test('unimplemented writes and unknown routes fail explicitly; roles are enforced', async () => {
    const { send } = await setup('player')
    expect((await send('/api/v1/admin/platform/users')).status).toBe(403)
    expect((await send('/api/v1/unknown')).status).toBe(404)
    expect((await send('/api/v1/auth/password', 'PUT', {})).status).toBe(501)
    const current = await (await send('/api/v1/auth/me')).json()
    expect(current.password).toBeUndefined()
    expect(current.login).toBeUndefined()
  })

  test('administrator-issued JWTs are ordinary stateless identity tokens', async () => {
    const { api, send } = await setup()
    const targetUserId = id(1, 3)
    expect((await send(
      `/api/v1/admin/platform/users/${targetUserId}/tokens`,
      'POST',
      { expiresInSeconds: 59 },
    )).status).toBe(400)
    const issuedResponse = await send(
      `/api/v1/admin/platform/users/${targetUserId}/tokens`,
      'POST',
      { expiresInSeconds: 3600 },
    )
    expect(issuedResponse.status).toBe(200)
    const issued = await issuedResponse.json()
    expect(issued.jwtId).toBeUndefined()
    const payload = JSON.parse(Buffer.from(issued.accessToken.split('.')[1]!, 'base64url').toString())
    expect(payload.impersonation).toBeUndefined()
    expect(payload.impersonator_id).toBeUndefined()
    const impersonated = await api.handle(new Request(base + '/api/v1/auth/me', {
      headers: { Authorization: `Bearer ${issued.accessToken}` },
    }))
    expect(impersonated.status).toBe(200)
    expect((await impersonated.json()).userId).toBe(targetUserId)

    expect((await send(`/api/v1/admin/platform/users/${targetUserId}/tokens`, 'DELETE')).status).toBe(200)
    const revoked = await api.handle(new Request(base + '/api/v1/auth/me', {
      headers: { Authorization: `Bearer ${issued.accessToken}` },
    }))
    expect(revoked.status).toBe(401)

    const administratorToken = await (await send(
      `/api/v1/admin/platform/users/${id(1)}/tokens`,
      'POST',
      { expiresInSeconds: 3600 },
    )).json()
    const nested = await api.handle(new Request(
      base + `/api/v1/admin/platform/users/${targetUserId}/tokens`,
      {
        method: 'POST',
        headers: { Authorization: `Bearer ${administratorToken.accessToken}`, 'Content-Type': 'application/json' },
        body: JSON.stringify({ expiresInSeconds: 3600 }),
      },
    ))
    expect(nested.status).toBe(200)
  })

  test('logout blocks cookie restore and rejects the old access token', async () => {
    const { api, send } = await setup()
    const logout = await send('/api/v1/auth/logout', 'POST')
    expect(logout.status).toBe(204)
    expect((await send('/api/v1/auth/me')).status).toBe(401)
    const restored = await api.handle(new Request(base + '/api/v1/auth/refresh', { method: 'POST', headers: { Cookie: 'noctf_mock_session=guest' } }))
    expect(restored.status).toBe(401)
  })

  test('pagination is stable and runtime reset replaces the simulated runtime ID', async () => {
    const { send } = await setup()
    const first = await (await send(`${competition}/gameplay-facts?limit=1`)).json()
    const next = await (await send(`${competition}/gameplay-facts?limit=1&offset=1`)).json()
    expect(first.items).toHaveLength(1)
    expect(next.items[0].id).not.toBe(first.items[0].id)
    const path = `${competition}/challenges/${id(4, 2)}/runtimes`
    const started = await (await send(path, 'POST', { replacesRuntimeId: null })).json()
    const afterStart = await (await send(path + '/current')).json()
    expect(afterStart.state).toBe('Running')
    expect(afterStart.accesses).toEqual([{ directAddress: 'tcp://challenge.mock.invalid:31002', webSocketAddress: null }])
    const reset = await (await send(path, 'POST', { replacesRuntimeId: started.runtimeInstanceId })).json()
    expect(reset.runtimeInstanceId).not.toBe(started.runtimeInstanceId)
    expect((await (await send(path + '/current')).json()).accesses).toEqual([{ directAddress: 'tcp://challenge.mock.invalid:31002', webSocketAddress: null }])
    await send(path + `/${reset.runtimeInstanceId}`, 'DELETE', {})
    expect((await (await send(path + '/current')).json()).state).toBe('Stopped')
  })

  test('SignalR handshake, group subscription and score invalidation work locally', async () => {
    const { api, request, send } = await setup('player')
    const hub = createMockRealtime(api)
    const negotiation = await (await hub(request('/hubs/v1/competitions/negotiate', 'POST', {}))).json()
    const path = '/hubs/v1/competitions?id=' + negotiation.connectionToken
    expect(await (await hub(request(path))).text()).toBe('')
    const post = (value: any) => hub(new Request(base + path, { method: 'POST', body: JSON.stringify(value) + '\x1e' }))
    await post({ protocol: 'json', version: 1 })
    expect(await (await hub(request(path))).text()).toBe('{}\x1e')
    await post({ type: 1, invocationId: '1', target: 'JoinCompetition', arguments: [id(2)] })
    expect(await (await hub(request(path))).text()).toContain('"type":3')
    await send(`${competition}/challenges/${id(4)}/flag-submissions`, 'POST', { flag: 'flag{mock_success}' })
    expect(await (await hub(request(path))).text()).toContain('scoreboardUpdated')
    expect((await hub(request(path, 'DELETE'))).status).toBe(202)
  })
})
