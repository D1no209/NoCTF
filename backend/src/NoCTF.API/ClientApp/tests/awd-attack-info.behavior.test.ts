import { expect, test } from 'bun:test'
import { computed, effectScope, nextTick, reactive, toRefs } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'
import { parseApiError } from '../app/utils/api-error'
import { message, translate } from '../app/utils/i18n'
import type { useAwdAttackInfo } from '../app/features/challenges/awd/useAwdAttackInfo'

const source = await Bun.file(new URL('../app/features/challenges/awd/useAwdAttackInfo.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source).replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await Promise.resolve(); await Promise.resolve() }
function harness() {
  const requests: { query: { offset: number }; signal: AbortSignal; resolve: (value: unknown) => void }[] = []
  let unmount = () => {}
  const deps = { computed, toRefs, useOffsetPagination, parseApiError, message, translate,
    listAwdAttackInfo: (input: object) => new Promise(resolve => requests.push({ ...input, resolve } as typeof requests[number])),
    watchCompetition: () => () => {}, onMounted: () => {}, onBeforeUnmount: (fn: () => void) => { unmount = fn },
    clearInterval: () => {}, document: { removeEventListener() {} } }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useAwdAttackInfo;`)(deps)
  const scope = effectScope(), state = scope.run(() => factory(reactive({ competitionId: 'competition',
    competitionChallengeId: 'challenge', teamId: 'target', teamName: 'Target' }), () => {})) as ReturnType<typeof useAwdAttackInfo>
  return { state, requests, stop() { unmount(); scope.stop() } }
}

test('pagination selects the requested page and revoked access clears locator contents', async () => {
  const app = harness()
  try {
    app.state.refresh(); await drain()
    app.requests[0]!.resolve({ data: { items: [{ flagId: 'one', publicLocator: '<script>plain text</script>' }], total: 13 } }); await drain()
    expect(app.state.rows.value[0]!.publicLocator).toBe('<script>plain text</script>')
    const next = app.state.loadPage(2); await drain()
    expect(app.requests[1]!.query.offset).toBe(10)
    app.requests[1]!.resolve({ error: { status: 403 }, response: { status: 403 } }); await next
    expect(app.state.rows.value).toEqual([])
    expect(app.state.error.value).not.toBeNull()
  } finally { app.stop() }
})

test('refresh aborts old requests and their late failures cannot erase the current page', async () => {
  const app = harness()
  try {
    app.state.refresh(); app.state.refresh(); await drain()
    expect(app.requests[0]!.signal.aborted).toBe(true)
    app.requests[1]!.resolve({ data: { items: [{ flagId: 'current' }], total: 1 } }); await drain()
    app.requests[0]!.resolve({ error: { status: 403 } }); await drain()
    expect(app.state.items.value[0]!.flagId).toBe('current')
    expect(app.state.error.value).toBeNull()
  } finally { app.stop() }
})
