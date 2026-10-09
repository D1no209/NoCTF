import { describe, expect, test } from 'bun:test'
import { effectScope, nextTick, ref } from 'vue'
import type { HubConnection } from '@microsoft/signalr'
import { useLiveSoloHub } from '../app/features/live-solo/useLiveSoloHub'

async function settle() { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }
function fixture(delay?: Promise<void>) {
  const operations: unknown[][] = [], handlers = new Map<string, (...args: any[]) => void>()
  const candidate = {
    async start() { operations.push(['start']); await delay },
    async stop() { operations.push(['stop']) },
    async invoke(...args: unknown[]) { operations.push(args) },
    on(name: string, handler: (...args: any[]) => void) { handlers.set(name, handler) },
    onreconnecting(handler: (...args: any[]) => void) { handlers.set('reconnecting', handler) },
    onreconnected(handler: (...args: any[]) => void) { handlers.set('reconnected', handler) },
    onclose(handler: (...args: any[]) => void) { handlers.set('closed', handler) },
  }
  const eligible = ref(true), competition = ref('competition'), match = ref<string | null>('match'), staff = ref(false)
  const scope = effectScope(); let refreshes = 0
  const state = scope.run(() => useLiveSoloHub(competition, match, staff, async () => { refreshes++ },
    { eligible, createConnection: () => candidate as unknown as HubConnection }))!
  return { state, scope, eligible, competition, match, staff, operations, handlers, refreshes: () => refreshes }
}
describe('LiveSolo private invalidations', () => {
  test('joins an explicit participant audience and refreshes only the exact current match', async () => {
    const f = fixture(); await settle()
    expect(f.operations).toContainEqual(['JoinMatch', 'competition', 'match', 0])
    expect(f.state.connected.value).toBe(true); expect(f.refreshes()).toBe(1)
    f.handlers.get('matchChanged')?.({ competitionId: 'other', matchId: 'match' }); await settle(); expect(f.refreshes()).toBe(1)
    f.handlers.get('matchChanged')?.({ competitionId: 'competition', matchId: 'match' }); await settle(); expect(f.refreshes()).toBe(2)
    f.scope.stop()
  })
  test('reconnect rejoins and rereads REST; revocation disposes the connection instead of reconnecting indefinitely', async () => {
    const f = fixture(); await settle()
    f.handlers.get('reconnecting')?.(); expect(f.state.connected.value).toBe(false)
    f.handlers.get('reconnected')?.(); await settle(); expect(f.refreshes()).toBe(2)
    f.eligible.value = false; await settle(); expect(f.state.connected.value).toBe(false)
    expect(f.operations).toContainEqual(['stop'])
    f.handlers.get('matchChanged')?.({ competitionId: 'competition', matchId: 'match' }); await settle(); expect(f.refreshes()).toBe(2)
    f.scope.stop()
  })
  test('a delayed connection completing after disposal cannot join or refresh', async () => {
    let resolve!: () => void
    const f = fixture(new Promise<void>(done => { resolve = done })); await nextTick(); f.scope.stop(); resolve(); await settle()
    expect(f.operations.some(x => x[0] === 'JoinMatch')).toBe(false); expect(f.refreshes()).toBe(0)
    expect(f.state.connected.value).toBe(false)
  })
})
