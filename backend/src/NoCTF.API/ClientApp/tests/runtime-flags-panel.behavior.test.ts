import { createTestApi, kiotaBindings, testId } from './support/kiota-harness'
import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, onScopeDispose, reactive, ref, watch } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { ApiError } from '../app/utils/api-error'
import type { NoCTFAPIEndpointsAdministrationRuntimeRuntimeFlagResponse } from '../app/api/models'

const source = await Bun.file(new URL('../app/features/admin/useRuntimeFlagsPanel.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
type Row = NoCTFAPIEndpointsAdministrationRuntimeRuntimeFlagResponse
const row = (id: string, overrides: Partial<Row> = {}): Row => ({ flag: { id: testId(id), flag: `flag{${id}}`, matchKind: 'Exact' }, source: 'Instance', state: 'Active', ...overrides })

function harness(fetch: (options: any) => Promise<any>, autoLoad = false) {
  const props = reactive({ runtimeId: 'runtime-a', autoLoad })
  const copied: string[] = []
  const dependencies = { ...kiotaBindings(source), api: createTestApi({ 'GET /api/v1/admin/runtimes/{runtimeInstanceId}/flags': fetch }),
computed,
onScopeDispose,
ref,
watch,
useOffsetPagination,
toast: { success: () => {}, error: () => {} },
translate: (key: string) => key,
describeMessage: (key: string) => ({ key }),
adminFormatDateTime: (value: string) => value,
parseApiError: (value: unknown) => new ApiError(value instanceof Error ? value.message : 'failed'),
navigator: { clipboard: { writeText: async (value: string) => { copied.push(value) } } } }
  const factory = new Function('deps', `const { ${Object.keys(dependencies).join(', ')} } = deps; ${compiled}; return useRuntimeFlagsPanel;`)(dependencies)
  const scope = effectScope()
  const state = scope.run(() => factory(props))!
  return { props, state, copied, stop: () => scope.stop() }
}

describe('runtime flag queries', () => {
  test('queries only on demand, masks content, supports reveal/copy and resets on runtime changes', async () => {
    const calls: any[] = []
    const app = harness(async options => { calls.push(options); return { items: [row('a')], total: 1 } })
    await drain()
    expect(calls).toHaveLength(0)
    await app.state.load()
    expect(calls[0].path.runtimeInstanceId).toBe('runtime-a')
    expect(calls[0].query.includeHistory).toBe(false)
    expect(app.state.rows.value[0].revealed).toBe(false)
    app.state.toggle(app.state.rows.value[0])
    expect(app.state.rows.value[0].revealed).toBe(true)
    await app.state.copy(app.state.rows.value[0])
    expect(app.copied).toEqual(['flag{a}'])
    app.props.runtimeId = 'runtime-b'
    await drain()
    expect(app.state.rows.value).toEqual([])
    expect(app.state.queried.value).toBe(false)
    expect(calls).toHaveLength(1)
    app.stop()
  })

  test('history changes and unmount reject late responses and discard flag contents', async () => {
    const requests: Array<{ options: any; resolve: (value: any) => void }> = []
    const app = harness(options => new Promise(resolve => requests.push({ options, resolve })), true)
    await drain()
    app.state.includeHistory.value = true
    await drain()
    expect(requests).toHaveLength(2)
    requests[0]!.resolve({ items: [row('stale')], total: 1 })
    await drain()
    expect(app.state.rows.value).toEqual([])
    requests[1]!.resolve({ items: [row('old', { state: 'Expired' })], total: 12 })
    await drain()
    expect(app.state.rows.value[0].state).toBe('Expired')
    expect(requests[1]!.options.query.includeHistory).toBe(true)
    const pending = app.state.loadPage(2)
    await drain()
    expect(requests[2]!.options.query.offset).toBe(10)
    app.stop()
    requests[2]!.resolve({ items: [row('late')], total: 12 })
    await pending
    expect(app.state.rows.value).toEqual([])
  })

  test('AWD round labels decode the canonical decimal specification and failures remain retryable', async () => {
    let failed = true
    const app = harness(async () => failed ? Promise.reject(new Error('Forbidden')) : { items: [row('round', { source: 'AwdRound', flag: { id: 'round', flag: 'rotation', specificationId: '00000012-0000-0000-0000-000000000000' } })], total: 1 })
    await drain()
    await app.state.load()
    expect(app.state.error.value).toBe('Forbidden')
    failed = false
    await app.state.load()
    expect(app.state.error.value).toBeNull()
    expect(app.state.rows.value[0].round).toBe(12)
    app.stop()
  })
})
