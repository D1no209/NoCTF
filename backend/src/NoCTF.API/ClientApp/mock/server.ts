import { spectatorMock } from './spectator'
import { createMockApi } from './api'
import { createMockRealtime } from './realtime'

const source = process.env.NOCTF_MOCK_TEAM_BAN
const api = createMockApi({ mfa: process.env.NOCTF_MOCK_MFA === 'true', teamBanSource: source === 'CheatIncident' || source === 'ManualModeration' ? source : undefined })
const realtime = createMockRealtime(api)
const server = Bun.serve({
  hostname: '127.0.0.1', port: Number(process.env.NOCTF_MOCK_API_PORT ?? 5081), idleTimeout: 30,
  async fetch(request) {
    try {
      const spectator = spectatorMock(request)
      if (spectator) {
        if (request.method === 'POST') for (const notify of api.changes) { notify('00000002-0000-4000-8000-000000000002'); notify('00000002-0000-4000-8000-000000000004') }
        return spectator
      }
      if (new URL(request.url).pathname.startsWith('/hubs/')) return await realtime(request)
      return await api.handle(request)
    }
    catch (error) {
      console.error('[Mock API]', request.method, new URL(request.url).pathname, error)
      return Response.json({ status: 500, title: 'Mock fixture error', detail: String(error) }, { status: 500 })
    }
  },
})
console.log(`Mock API: ${server.url}\nMock site: http://127.0.0.1:3001\nDefault identity: admin. Demo accounts: admin / organizer / player, password: Mock123!\nAll changes are in memory. Restart to reset. No upstream forwarding.`)
