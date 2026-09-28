import type { createMockApi } from './api'

/** Minimal SignalR JSON/LongPolling transport for the existing frontend client. */
export function createMockRealtime(api: ReturnType<typeof createMockApi>) {
  type Connection = { queue: string[]; groups: Set<string>; hub: string; wake?: () => void; fresh: boolean }
  const connections = new Map<string, Connection>()
  const send = (client: Connection, message: unknown) => {
    client.queue.push(JSON.stringify(message) + '\x1e')
    client.wake?.()
  }
  api.changes.add(competitionId => {
    for (const client of connections.values()) if (client.groups.has(competitionId)) {
      send(client, { type: 1, target: 'scoreboardUpdated', arguments: [{ competitionId, version: String(api.state.facts.length + 1), schemaRevision: 'mock-1', challengeCatalogRevision: 'mock-1' }] })
    }
  })
  api.notificationChanges.add(() => {
    for (const client of connections.values()) if (client.hub === 'notifications')
      send(client, { type: 1, target: 'notificationChanged', arguments: [] })
  })
  return async function handle(request: Request) {
    const url = new URL(request.url)
    if (!/^\/hubs\/v1\/(competitions|notifications|admin\/platform-logs)(\/negotiate)?$/.test(url.pathname)) return new Response(null, { status: 404 })
    if (url.pathname.endsWith('/negotiate') && request.method === 'POST') {
      if (!api.userFor(request)) return new Response(null, { status: 401 })
      const token = crypto.randomUUID()
      connections.set(token, { queue: [], groups: new Set(), hub: url.pathname.split('/')[3]!, fresh: true })
      return Response.json({ negotiateVersion: 1, connectionId: token, connectionToken: token, availableTransports: [{ transport: 'LongPolling', transferFormats: ['Text'] }] })
    }
    const token = url.searchParams.get('id') ?? ''
    const client = connections.get(token)
    if (!client) return new Response(null, { status: 404 })
    if (request.method === 'DELETE') { connections.delete(token); client.wake?.(); return new Response(null, { status: 202 }) }
    if (request.method === 'POST') {
      for (const chunk of (await request.text()).split('\x1e').filter(Boolean)) {
        let message: any
        try { message = JSON.parse(chunk) } catch { return new Response(null, { status: 400 }) }
        if (message.protocol === 'json') send(client, {})
        else if (message.type === 1) {
          if (message.target === 'JoinCompetition') client.groups.add(message.arguments?.[0])
          if (message.target === 'LeaveCompetition') client.groups.delete(message.arguments?.[0])
          if (message.invocationId !== undefined) send(client, { type: 3, invocationId: message.invocationId, result: null })
        }
      }
      return new Response(null, { status: 200 })
    }
    if (request.method === 'GET') {
      if (client.fresh) { client.fresh = false; return new Response('') }
      if (!client.queue.length) await new Promise<void>(resolve => {
        const finish = () => { clearTimeout(timeout); request.signal.removeEventListener('abort', finish); client.wake = undefined; resolve() }
        const timeout = setTimeout(finish, 12_000)
        client.wake = finish
        request.signal.addEventListener('abort', finish, { once: true })
      })
      return new Response(client.queue.splice(0).join('') || '{"type":6}\x1e', { headers: { 'Content-Type': 'text/plain', 'Cache-Control': 'no-store' } })
    }
    return new Response(null, { status: 405 })
  }
}
