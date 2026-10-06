import { expect, test } from 'bun:test'
import { createMockApi } from './api'
import { id } from './schema'

const base = 'http://127.0.0.1:5081/api/v1'
const root = `/competitions/${id(2)}`
async function signIn(api: ReturnType<typeof createMockApi>, login: string) {
  const response = await api.handle(new Request(`${base}/auth/login`, { method: 'POST',
    headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login, password: 'Mock123!' }) }))
  const { accessToken } = await response.json()
  return (path: string, method = 'GET', body?: unknown) => api.handle(new Request(base + path, {
    method, headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' },
    ...(body ? { body: JSON.stringify(body) } : {}),
  }))
}

test('Mock ban preview preserves source, captain appeal, duplicate protection and unban state', async () => {
  const api = createMockApi({ teamBanSource: 'CheatIncident' })
  const player = await signIn(api, 'player')
  const admin = await signIn(api, 'admin')
  const ban = await (await player(root + '/team-ban-case')).json()
  expect(ban.source).toBe('CheatIncident')
  expect(ban.canAppeal).toBe(true)
  expect((await admin(root + '/team-ban-appeals', 'POST', { statement: 'A valid appeal statement for this contest.' })).status).toBe(403)
  expect((await player(root + '/team-ban-appeals', 'POST', { statement: 'short' })).status).toBe(400)
  expect((await player(root + '/team-ban-appeals', 'POST', { statement: 'A valid appeal statement for this contest.' })).status).toBe(204)
  expect((await (await player(root + '/team-ban-case')).json()).appeal.status).toBe('Submitted')
  expect((await player(root + '/team-ban-appeals', 'POST', { statement: 'A duplicate appeal for this contest.' })).status).toBe(409)
  await admin(root + `/teams/${ban.teamId}`, 'PATCH', { ban: { isBanned: false } })
  const unbanned = await (await player(root + '/team-ban-case')).json()
  expect(unbanned.isCurrentlyBanned).toBe(false)
  expect(unbanned.canAppeal).toBe(false)
})

test('Mock defaults are unbanned and manual bans use the neutral source', async () => {
  const api = createMockApi()
  const player = await signIn(api, 'player')
  const admin = await signIn(api, 'admin')
  expect((await player(root + '/team-ban-case')).status).toBe(404)
  const team = await (await player(root + '/teams/me')).json()
  await admin(root + `/teams/${team.id}`, 'PATCH', { ban: { isBanned: true } })
  expect((await (await player(root + '/team-ban-case')).json()).source).toBe('ManualModeration')
})
