import { expect, test } from 'bun:test'
import { createMockApi } from './api'
import { createMockRealtime } from './realtime'
import { spectatorMock } from './spectator'

const base = 'http://127.0.0.1:5081'
const competition = '00000002-0000-4000-8000-000000000002'
test('anonymous spectator SignalR never receives scoreboard versions or staff payloads', async () => {
  const api = createMockApi(), hub = createMockRealtime(api)
  const negotiation = await (await hub(new Request(`${base}/hubs/v1/spectators/negotiate`, { method: 'POST' }))).json()
  const path = `${base}/hubs/v1/spectators?id=${negotiation.connectionToken}`
  expect(await (await hub(new Request(path))).text()).toBe('')
  await hub(new Request(path, { method: 'POST', body: JSON.stringify({ type: 1, invocationId: '1', target: 'JoinSpectator', arguments: [competition] }) + '\x1e' }))
  await hub(new Request(path))
  for (const notify of api.changes) notify(competition)
  const payload = await (await hub(new Request(path))).text()
  expect(JSON.parse(payload.trim().slice(0, -1))).toEqual({ type: 1, target: 'spectatorChanged', arguments: [competition] })
  await hub(new Request(path, { method: 'DELETE' }))
  expect((await hub(new Request(`${base}/hubs/v1/competitions/negotiate`, { method: 'POST' }))).status).toBe(401)
})

test('frozen mock keeps scores, attacks, services and control observations together; hidden clears them', async () => {
  spectatorMock(new Request(`${base}/__mock/spectator?scope=Frozen`, { method: 'POST' }))
  const request = new Request(`${base}/api/v1/competitions/${competition}/awd-spectator?minutes=30`)
  const first = await spectatorMock(request)!.json(), second = await spectatorMock(request)!.json()
  for (const field of ['version', 'dataAsOf', 'teams', 'services', 'changes', 'settlements']) expect(second[field]).toEqual(first[field])
  spectatorMock(new Request(`${base}/__mock/spectator?scope=Hidden`, { method: 'POST' }))
  const hidden = await spectatorMock(request)!.json()
  for (const field of ['teams', 'challenges', 'services', 'points', 'changes', 'settlements']) expect(hidden[field]).toEqual([])
  spectatorMock(new Request(`${base}/__mock/spectator?scope=Live`, { method: 'POST' }))
})
