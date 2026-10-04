import { expect, test } from 'bun:test'

const source = await Bun.file(new URL('../app/composables/useCompetitionHub.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')
  .replaceAll('import.meta.dev', 'true')

function harness() {
  let reconnect = async () => {}
  let startCount = 0
  const invocations: string[][] = []
  const hub = {
    state: 'Disconnected',
    start: async () => { startCount++; hub.state = 'Connected' },
    stop: async () => { hub.state = 'Disconnected' },
    on: () => {}, onreconnecting: () => {}, onclose: () => {},
    onreconnected: (handler: () => Promise<void>) => { reconnect = handler },
    invoke: async (...args: string[]) => { invocations.push(args) },
  }
  class Builder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() { return hub }
  }
  const deps = {
    signalR: { HubConnectionBuilder: Builder, HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' }, HttpTransportType: { ServerSentEvents: 1, LongPolling: 2 }, LogLevel: { Warning: 1 } },
    getAccessToken: () => 'test-token', getRealtimeAccessToken: async () => 'test-token',
    currentLocale: () => 'zh-CN',
    startRealtimeWithRetry: async (start: () => Promise<void>) => { await start(); return true },
    setInterval: () => 1, clearInterval: () => {},
  }
  const watch = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return watchCompetition;`)(deps) as (id: string, handlers: { onReconnected: () => void }) => () => void
  return { watch, reconnect: () => reconnect(), invocations, starts: () => startCount }
}

const drain = () => new Promise(resolve => setTimeout(resolve, 0))

test('new subscribers reuse the live Hub without refreshing every existing consumer', async () => {
  const app = harness()
  let firstRefresh = 0
  let secondRefresh = 0
  const first = app.watch('competition-a', { onReconnected: () => firstRefresh++ })
  await drain()
  expect(firstRefresh).toBe(1)
  const second = app.watch('competition-a', { onReconnected: () => secondRefresh++ })
  await drain()
  expect(firstRefresh).toBe(1)
  expect(secondRefresh).toBe(0)
  expect(app.starts()).toBe(1)
  expect(app.invocations.filter(([method]) => method === 'JoinCompetition')).toHaveLength(1)
  await app.reconnect()
  await drain()
  expect(firstRefresh).toBe(2)
  expect(secondRefresh).toBe(1)
  first(); second()
})

test('joining a different competition only joins the new group', async () => {
  const app = harness()
  let firstRefresh = 0
  const first = app.watch('competition-a', { onReconnected: () => firstRefresh++ })
  await drain()
  const second = app.watch('competition-b', { onReconnected: () => {} })
  await drain()
  expect(firstRefresh).toBe(1)
  expect(app.invocations.filter(([method]) => method === 'JoinCompetition')).toEqual([
    ['JoinCompetition', 'competition-a'], ['JoinCompetition', 'competition-b'],
  ])
  first(); second()
})
