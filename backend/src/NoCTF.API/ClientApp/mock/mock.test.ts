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
      const runtime = await (await send(`${competition}/challenges/${challenge.id}/runtime`)).json()
      expect(runtime.state).toBe('Running')
      expect(runtime.urls).toHaveLength(1)
      expect(runtime.urls[0]).toMatch(/^tcp:\/\/challenge\.mock\.invalid:31\d{3}$/)
      sockets.add(runtime.urls[0])
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

  test('correct / wrong / duplicate flags update records and scores without double scoring', async () => {
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
    expect(await submit('flag{mock_success}')).toBe('Duplicate')
    const board = await (await send(`${competition}/leaderboard`)).json()
    expect(board.teams.find((team: Data) => team.teamId === id(5)).totalScore).toBe(200)
    const facts = await (await send(`${competition}/gameplay-facts`)).json()
    expect(facts.items[0].result).toBe('Duplicate')
  })

  test('profile and platform edits persist in memory and reset with a fresh Mock instance', async () => {
    const { send } = await setup()
    await send('/api/v1/auth/me/profile', 'PUT', { userName: 'Changed Mock Admin', description: 'Mock profile' })
    expect((await (await send('/api/v1/auth/me')).json()).userName).toBe('Changed Mock Admin')
    await send('/api/v1/admin/platform/configuration', 'PUT', { name: 'Mock edited', description: 'Local' })
    expect((await (await send('/api/v1/platform/configuration')).json()).name).toBe('Mock edited')
    const fresh = await setup()
    expect((await (await fresh.send('/api/v1/platform/configuration')).json()).name).toBe('NoCTF · MOCK')
  })

  test('monitoring demo exposes a complete and varied live snapshot', async () => {
    const { send } = await setup()
    const response = await send('/api/v1/admin/platform/monitoring')
    expect(response.status).toBe(200)
    const snapshot = await response.json()
    expect(snapshot.status).toBe(1)
    expect(snapshot.prometheusAvailable).toBe(true)
    expect(snapshot.natsAvailable).toBe(true)
    expect(snapshot.metrics).toHaveLength(23)
    expect(new Set(snapshot.metrics.map((item: Data) => item.kind)).size).toBe(23)
    expect(snapshot.metrics.some((item: Data) => item.status === 1)).toBe(true)
    expect(snapshot.latencyDetails).toHaveLength(5)
    expect(snapshot.poolResources).toHaveLength(6)
  })

  test('wallpaper upload, protected read and preference changes share account state', async () => {
    const { api, accessToken, send } = await setup()
    const before = await (await send('/api/v1/auth/me')).json()
    expect(before.wallpaperRevision).toBeNull()
    expect(before.wallpaperEnabled).toBe(false)
    expect((await send('/api/v1/auth/me/wallpaper-preference', 'PUT', { enabled: true })).status).toBe(400)

    const form = new FormData()
    form.set('file', new File(['mock wallpaper'], 'wallpaper.png', { type: 'image/png' }))
    const uploaded = await api.handle(new Request(base + '/api/v1/auth/me/wallpaper', {
      method: 'POST',
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
    const disabled = await (await send('/api/v1/auth/me/wallpaper-preference', 'PUT', { enabled: false })).json()
    expect(disabled.wallpaperEnabled).toBe(false)
    expect(disabled.wallpaperRevision).toBe(current.wallpaperRevision)
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
    const next = await (await send(`${competition}/gameplay-facts?limit=1&cursor=${first.nextCursor}`)).json()
    expect(first.items).toHaveLength(1)
    expect(next.items[0].id).not.toBe(first.items[0].id)
    const path = `${competition}/challenges/${id(4, 2)}/runtime`
    const started = await (await send(path + '/start', 'POST', {})).json()
    const afterStart = await (await send(path)).json()
    expect(afterStart.state).toBe('Running')
    expect(afterStart.urls).toEqual(['tcp://challenge.mock.invalid:31002'])
    const reset = await (await send(path + '/reset', 'POST', {})).json()
    expect(reset.runtimeInstanceId).not.toBe(started.runtimeInstanceId)
    expect((await (await send(path)).json()).urls).toEqual(['tcp://challenge.mock.invalid:31002'])
    await send(path + '/stop', 'POST', {})
    expect((await (await send(path)).json()).state).toBe('Stopped')
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
