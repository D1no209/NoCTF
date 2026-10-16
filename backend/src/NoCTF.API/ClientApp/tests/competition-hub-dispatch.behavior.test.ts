import { expect, test } from 'bun:test'

const source = await Bun.file(new URL('../app/composables/useCompetitionHub.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')
  .replace(/export /g, '')
  .replaceAll('import.meta.dev', 'true')

interface Payload { competitionId?: string }

function harness() {
  const handlers = new Map<string, (payload: unknown) => void>()
  const hub = {
    state: 'Disconnected',
    start: async () => { hub.state = 'Connected' },
    stop: async () => { hub.state = 'Disconnected' },
    on: (name: string, handler: (payload: unknown) => void) => { handlers.set(name, handler) },
    onreconnecting: () => {}, onclose: () => {}, onreconnected: () => {},
    invoke: async () => {},
  }
  class Builder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { return hub }
  }
  const deps = {
    signalR: { HubConnectionBuilder: Builder, HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' }, HttpTransportType: { ServerSentEvents: 1, LongPolling: 2 }, LogLevel: { Warning: 0 } },
    getAccessToken: () => 'test-token', getRealtimeAccessToken: async () => 'test-token',
    currentLocale: () => 'zh-CN',
    startRealtimeWithRetry: async (start: () => Promise<void>) => { await start(); return true },
    setInterval: () => 1, clearInterval: () => {},
  }
  const body = 'const { ' + Object.keys(deps).join(', ') + ' } = deps; ' + compiled + '; return watchCompetition;'
  const watch = new Function('deps', body)(deps) as (id: string, handlers: Record<string, (payload: unknown) => void>) => () => void
  return { watch, emit: (name: string, payload: Payload) => handlers.get(name)?.(payload) }
}

const drain = () => new Promise(resolve => setTimeout(resolve, 0))

test('a grouped event without a competition id is never broadcast to every subscriber', async () => {
  const app = harness()
  const seen: string[] = []
  const first = app.watch('competition-a', { scoreboardUpdated: () => seen.push('a') })
  const second = app.watch('competition-b', { scoreboardUpdated: () => seen.push('b') })
  await drain()

  app.emit('scoreboardUpdated', { competitionId: undefined })
  await drain()
  expect(seen).toEqual([])

  app.emit('scoreboardUpdated', { competitionId: 'competition-a' })
  await drain()
  expect(seen).toEqual(['a'])

  first(); second()
})
