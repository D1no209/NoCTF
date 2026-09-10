import { createMockApi } from './api'
import { createMockRealtime } from './realtime'

const api = createMockApi()
const realtime = createMockRealtime(api)
const server = Bun.serve({
  hostname: '127.0.0.1', port: 5081, idleTimeout: 30,
  async fetch(request) {
    try {
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
