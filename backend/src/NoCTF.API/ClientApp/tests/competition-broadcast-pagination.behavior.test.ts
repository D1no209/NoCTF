import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, reactive, ref, toRefs, watch } from 'vue'
import { createTrailingRefresh } from '../app/lib/latest-page-refresh'
import { parseApiError } from '../app/utils/api-error'
import * as broadcast from '../app/utils/competition-broadcast'

const source = await Bun.file(new URL('../app/features/competition/useCompetitionBroadcastPanel.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export /g, '')
const factory = (deps: Record<string, unknown>) => new Function('deps',
  `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useCompetitionBroadcastPanel;`)(deps)
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
const event = (id: string, time: number) => ({ id, kind: 'ChallengePublished', competitionChallengeId: id, occurredAt: new Date(time).toISOString() })

async function harness(count = 65, startDays = 2) {
  const now = Date.now()
  const events = Array.from({ length: count }, (_, i) => event(`event-${i}`, now - (i + 1) * 60_000))
  const competition = ref({ status: 'Running', startTime: new Date(now - startDays * 86400_000).toISOString() })
  const props = reactive({ competitionId: 'competition', fill: true })
  const requests: any[] = []
  let fail = false
  let gate: Promise<void> | null = null
  let mounted!: () => void
  let unmounted!: () => void
  let handlers: any
  const scope = effectScope()
  const state = scope.run(() => factory({
    ...broadcast, computed, ref, watch, toRefs, createTrailingRefresh, parseApiError, Megaphone: {},
    competitionContextKey: {}, inject: () => ({ competition }),
    describeMessage: (key: string) => ({ key }), motionAttributes: () => ({}),
    onMounted: (fn: () => void) => { mounted = fn }, onUnmounted: (fn: () => void) => { unmounted = fn },
    watchCompetition: (_: string, callbacks: any) => { handlers = callbacks; return () => {} },
    listCompetitionEvents: async ({ path, query }: any) => {
      requests.push({ ...query, competitionId: path.competitionId })
      const pending = gate; gate = null
      if (pending) await pending
      if (fail) { fail = false; return { error: { status: 503 } } }
      const rows = events.filter(e => Date.parse(e.occurredAt) >= Date.parse(query.from) && Date.parse(e.occurredAt) <= Date.parse(query.to))
        .sort((a, b) => Date.parse(b.occurredAt) - Date.parse(a.occurredAt))
      const offset = query.cursor ? rows.findIndex(e => e.id === query.cursor) + 1 : 0
      const page = rows.slice(offset, offset + query.limit)
      return { data: { items: page, nextCursor: offset + page.length < rows.length ? page.at(-1)?.id : null } }
    },
  })(props))!
  mounted()
  await drain()
  return { state, props, competition, requests, events, now,
    reconnect: () => handlers.onReconnected(),
    failNext: () => { fail = true },
    defer: () => { let release!: () => void; gate = new Promise<void>(resolve => { release = resolve }); return release },
    stop: () => { unmounted(); scope.stop() },
  }
}

describe('broadcast incremental history', () => {
  test('fetches one bounded page per demand with a fixed cursor window', async () => {
    const app = await harness()
    expect(app.requests).toHaveLength(1)
    expect(app.state.items.value).toHaveLength(20)
    await app.state.loadMore()
    expect(app.state.items.value).toHaveLength(40)
    expect(app.requests[1]).toMatchObject({ limit: 20, from: app.requests[0].from, to: app.requests[0].to, cursor: 'event-19' })
    await app.state.loadMore(); await app.state.loadMore()
    expect(app.state.items.value).toHaveLength(65)
    expect(app.state.hasMore.value).toBeFalse()
    app.stop()
  })

  test('retains history and fills reconnect gaps larger than a batch', async () => {
    const app = await harness()
    await app.state.loadMore()
    app.events.push(...Array.from({ length: 35 }, (_, i) => event(`new-${i}`, app.now + i * 1000)))
    app.reconnect(); await drain()
    expect(app.state.items.value).toHaveLength(60)
    await app.state.loadMore()
    expect(app.state.items.value).toHaveLength(75)
    await app.state.loadMore(); await app.state.loadMore()
    expect(app.state.items.value).toHaveLength(100)
    expect(new Set(app.state.items.value.map((e: any) => e.id)).size).toBe(100)
    app.stop()
  })

  test('walks older windows without dropping their inclusive boundary', async () => {
    const app = await harness(0, 80)
    const boundary = Date.parse(app.requests[0].from)
    app.events.push(event('boundary', boundary), event('old', app.now - 65 * 86400_000))
    await app.state.loadMore()
    expect(app.requests[1].to).toBe(app.requests[0].from)
    await app.state.loadMore()
    expect(app.state.items.value.map((e: any) => e.id)).toEqual(['boundary', 'old'])
    expect(app.state.hasMore.value).toBeFalse()
    app.stop()
  })

  test('keeps a failed cursor retryable and prevents duplicate concurrent reads', async () => {
    const app = await harness()
    app.failNext(); await app.state.loadMore()
    expect(app.state.items.value).toHaveLength(20)
    expect(app.state.historyError.value).not.toBeNull()
    const release = app.defer()
    const pending = app.state.loadMore()
    await app.state.loadMore()
    expect(app.requests).toHaveLength(3)
    expect(app.requests[2].cursor).toBe(app.requests[1].cursor)
    release(); await pending
    expect(app.state.items.value).toHaveLength(40)
    app.stop()
  })

  test('discards an old competition response after switching the route', async () => {
    const app = await harness()
    const release = app.defer()
    const pending = app.state.loadMore()
    app.props.competitionId = 'other'
    await drain()
    expect(app.requests.at(-1).competitionId).toBe('other')
    release(); await pending
    expect(app.state.items.value).toHaveLength(20)
    app.stop()
  })
})
