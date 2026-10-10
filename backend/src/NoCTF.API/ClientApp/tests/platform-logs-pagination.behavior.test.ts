import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, ref, watch } from 'vue'
import { useCursorPagePagination } from '../app/composables/useCursorPagePagination'
import { parseApiError } from '../app/utils/api-error'

const source = await Bun.file(new URL('../app/features/routes/admin/platform/useAdminPlatformLogsPage.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export /g, '')
const factory = (deps: Record<string, unknown>) => new Function('deps',
  `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAdminPlatformLogsPage;`)(deps)
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

async function harness() {
  const calls: any[] = []
  const logs = Array.from({ length: 125 }, (_, i) => ({ cursor: `log-${i}`, timestamp: new Date(Date.now() - (i + 1) * 60_000).toISOString(),
    level: 'Warning', service: 'Runner', message: `sample-${i}`, category: 'Mock' }))
  let mount!: () => void, unmount!: () => void, liveEvent!: (log: any) => void
  const scope = effectScope()
  const state = scope.run(() => factory({
    ref, watch, computed, markRaw, useCursorPagePagination, parseApiError,
    Download: {}, Radio: {}, AdminDateTimeComponent: {}, translate: (key: string) => key,
    onMounted: (fn: () => void) => { mount = fn }, onUnmounted: (fn: () => void) => { unmount = fn },
    usePlatformLogHub: (fn: typeof liveEvent) => { liveEvent = fn; return { state: ref('connected'), start: async () => {}, stop: async () => {} } },
    adminPlatformListLogs: async ({ query }: any) => {
      calls.push({ ...query })
      const offset = query.cursor ? logs.findIndex(log => log.cursor === query.cursor) + 1 : 0
      const rows = logs.slice(offset, offset + query.limit)
      return { data: { items: rows, nextCursor: offset + rows.length < logs.length ? rows.at(-1)?.cursor : null } }
    },
  })())!
  mount(); await drain()
  return { state, calls, emit: liveEvent, stop: () => { unmount(); scope.stop() } }
}

describe('platform log page navigation', () => {
  test('loads only the selected page and resets to page one after changing its limit', async () => {
    const app = await harness()
    expect(app.state.items.value).toHaveLength(50)
    await app.state.loadPage(2)
    expect(app.state.items.value[0].cursor).toBe('log-50')
    expect(app.state.items.value).toHaveLength(50)
    await app.state.loadPage(1)
    expect(app.state.items.value[0].cursor).toBe('log-0')
    app.state.setPageSize(20); await drain()
    expect(app.state.page.value).toBe(1)
    expect(app.state.items.value).toHaveLength(20)
    expect(app.calls.at(-1)).toMatchObject({ limit: 20, cursor: null })
    app.stop()
  })

  test('binds previous and next cursors to the applied filters and fixed time window', async () => {
    const app = await harness()
    app.state.minimumLevel.value = 'Error'
    app.state.search.value = 'new filter'
    await app.state.loadPage(2)
    expect(app.calls[1]).toMatchObject({ minimumLevel: 'Warning', search: null, from: app.calls[0].from, to: app.calls[0].to })
    app.state.applyFilters(); await drain()
    expect(app.calls.at(-1)).toMatchObject({ minimumLevel: 'Error', search: 'new filter', cursor: null })
    app.stop()
  })

  test('does not evict history on realtime arrivals, counts matching logs, and refreshes on demand', async () => {
    const app = await harness()
    await app.state.loadPage(2)
    const cursors = app.state.items.value.map((log: any) => log.cursor)
    const arrival = { cursor: 'new', timestamp: new Date().toISOString(), level: 'Error', service: 'Runner', message: 'new error', category: 'Mock' }
    app.emit(arrival); app.emit(arrival)
    app.emit({ ...arrival, cursor: 'debug', level: 'Debug' })
    expect(app.state.newLogs.value).toBe(1)
    expect(app.state.items.value.map((log: any) => log.cursor)).toEqual(cursors)
    app.state.viewLatest(); await drain()
    expect(app.state.page.value).toBe(1)
    expect(app.state.newLogs.value).toBe(0)
    expect(app.calls.at(-1).cursor).toBeNull()
    app.state.live.value = false; app.emit({ ...arrival, cursor: 'disabled' })
    expect(app.state.newLogs.value).toBe(0)
    app.stop()
  })
})
