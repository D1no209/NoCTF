import { describe, expect, test } from 'bun:test'
import { computed, effectScope, nextTick, ref, shallowRef, watch } from 'vue'
import { SpectatorChangeWindow } from '../app/features/spectator/spectator-sync'
import { controlIntervals } from '../app/features/spectator/spectator-timeline'
import { spectatorServiceState } from '../app/features/spectator/spectator-service-state'
import type { NoCtfapiEndpointsCompetitionsSpectatorScreenResponse as Screen } from '../app/api'
import type { useSpectatorScreen } from '../app/features/spectator/useSpectatorScreen'
import type { SpectatorSceneState, SpectatorBeam } from '../app/lib/spectator-scene'

const source = await Bun.file(new URL('../app/features/spectator/useSpectatorScreen.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source.replaceAll('import.meta.dev', 'false'))
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
const drain = async () => { await nextTick(); await Promise.resolve(); await Promise.resolve() }
const data = (version: number, scope: 'Live' | 'Frozen' | 'Hidden' = 'Live'): Screen => ({
  version: String(version), scope, mode: 'Awd', dataAsOf: '2026-10-12T00:10:00Z', serverTime: '2026-10-12T00:10:00Z',
  teams: [{ id: 'a', name: 'A', score: 100 }, { id: 'b', name: 'B', score: 80 }], challenges: [{ competitionChallengeId: 'c', title: 'C' }], changes: [],
})

function harness() {
  const requests: { query: { cursor?: string }; signal: AbortSignal; resolve: (value: unknown) => void }[] = []
  const timers = new Map<number, () => void>(), cleanup: (() => void)[] = []
  let timer = 0
  class Scene {
    states: SpectatorSceneState[] = []
    attacks: SpectatorBeam[] = []
    update(state: SpectatorSceneState) { this.states.push(state) }
    enqueue(events: SpectatorBeam[]) { this.attacks.push(...events) }
    dispose() {}
  }
  let scene: Scene
  const endpoint = (options: { query: { cursor?: string }; signal: AbortSignal }) => new Promise(resolve => requests.push({ ...options, resolve }))
  const deps = {
    computed, ref, shallowRef, watch, SpectatorChangeWindow, controlIntervals, spectatorServiceState,
    SpectatorScene: class extends Scene { constructor() { super(); scene = this } },
    getAwdSpectatorEndpoint: endpoint, getKohSpectatorEndpoint: endpoint,
    useRoute: () => ({ params: { id: 'competition' }, query: {} }),
    useLocale: () => ({ t: (key: string) => key }), useHead: () => {},
    onMounted: () => {}, onBeforeUnmount: (fn: () => void) => cleanup.push(fn),
    document: { hidden: false, removeEventListener() {} },
    setTimeout: (fn: () => void) => { timers.set(++timer, fn); return timer }, clearTimeout: (id: number) => timers.delete(id),
    clearInterval: () => {},
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useSpectatorScreen;`)(deps)
  const scope = effectScope()
  const state = scope.run(() => factory('Awd')) as ReturnType<typeof useSpectatorScreen>
  state.mountScene({} as HTMLElement)
  return { state, requests, timers, scene: scene!, stop() { cleanup.forEach(fn => fn()); scope.stop() },
    respond(index: number, screen?: Screen, status = 200) { requests[index]!.resolve({ data: screen, response: { status } }) } }
}

describe('spectator snapshot controller', () => {
  test('one server per question uses authoritative compromise even with empty history, and resets with the next snapshot', async () => {
    const h = harness(), first = h.state.refresh()
    const screen = { ...data(1), challenges: ['c', 'd', 'e'].map(competitionChallengeId => ({ competitionChallengeId })),
      services: [{ teamId: 'a', challengeId: 'c', health: 'Ok' as const, isCompromised: true },
        { teamId: 'a', challengeId: 'd', health: 'Offline' as const, isCompromised: true }] }
    h.respond(0, screen); await first; await drain()
    expect(h.scene.states.at(-1)!.nodes.map(node => node.services?.length)).toEqual([3, 3])
    expect(h.scene.states.at(-1)!.nodes[0]!.services).toEqual(['Ok', 'Offline', 'Unknown'])
    expect(h.scene.states.at(-1)!.nodes[1]!.services).toEqual(['Unknown', 'Unknown', 'Unknown'])
    expect(h.scene.states.at(-1)!.nodes[0]!.compromised).toEqual([true, true, false])
    expect(h.state.services.value.map(service => service.state)).toEqual(['Ok', 'Offline', 'Unknown'])
    expect(h.state.healthText(h.state.services.value[0]!.state)).toBe('spectator.ok')
    expect(h.state.fallbackRows.value[0]!.services.map(service => service.health)).toEqual(['Ok', 'Offline', 'Unknown'])
    const second = h.state.refresh()
    h.respond(1, { ...screen, version: '2', services: [{ teamId: 'a', challengeId: 'c', health: 'Ok', isCompromised: false }] })
    await second; await drain()
    expect(h.scene.states.at(-1)!.nodes[0]!.services).toEqual(['Ok', 'Unknown', 'Unknown'])
    h.stop()
  })
  test('all health conclusions remain independent of capture and settlement across a frozen read', async () => {
    const h = harness(), first = h.state.refresh()
    const healths = ['Ok', 'Mumble', 'Offline', 'Recovering', 'InternalError'] as const
    const screen: Screen = { ...data(2), challenges: healths.map(competitionChallengeId => ({ competitionChallengeId })),
      services: healths.map(health => ({ teamId: 'a', challengeId: health, health, enabled: true,
        execution: health === 'InternalError' ? 'FailedToDetermine' : 'Completed',
        settlement: health === 'InternalError' ? 'PendingAdjudication' : 'Final', isCompromised: true })) }
    h.respond(0, screen); await first; await drain()
    expect(h.scene.states.at(-1)!.nodes[0]!.services).toEqual([...healths].sort())
    expect(h.scene.states.at(-1)!.nodes[0]!.compromised).toEqual(healths.map(() => true))
    expect(h.state.services.value.find(service => service.health === 'InternalError')?.settlement).toBe('PendingAdjudication')
    const freeze = h.state.refresh()
    h.respond(1, { ...screen, version: '1', scope: 'Frozen' }); await freeze; await drain()
    expect(h.state.snapshot.value?.scope).toBe('Frozen')
    expect(h.scene.states.at(-1)!.reduced).toBe(true)
    expect(h.scene.states.at(-1)!.nodes[0]!.services).toEqual([...healths].sort())
    expect(h.scene.states.at(-1)!.nodes[0]!.compromised).toEqual(healths.map(() => true))
    expect(spectatorServiceState({ enabled: true, health: null, execution: 'Pending', lastTrustedHealth: 'Ok' })).toBe('Pending')
    expect(spectatorServiceState({ enabled: true, health: null, execution: 'Running' })).toBe('Running')
    expect(spectatorServiceState({ enabled: false, health: null })).toBe('Disabled')
    h.stop()
  })
  test('coalesces bursts into a single trailing request and never overlaps reads', async () => {
    const h = harness()
    const first = h.state.refresh()
    await h.state.refresh(); await h.state.refresh()
    expect(h.requests).toHaveLength(1)
    h.respond(0, data(1)); await first
    expect(h.timers.size).toBe(1)
    h.timers.values().next().value!()
    expect(h.requests).toHaveLength(2)
    h.respond(1, data(2)); await drain()
    expect(h.state.snapshot.value?.version).toBe('2')
    h.stop()
  })
  test('publishes pages atomically, including a gap larger than 256, without replaying the initial window', async () => {
    const h = harness(), first = h.state.refresh()
    const event = (n: number) => ({ id: String(n), at: '2026-10-12T00:01:00Z', kind: 'Attack' as const, teamId: 'a', targetTeamId: 'b', challengeId: 'c' })
    h.respond(0, { ...data(1), changes: [event(0)] }); await first
    const second = h.state.refresh()
    h.respond(1, { ...data(2), changes: Array.from({ length: 256 }, (_, n) => event(n + 1)), nextCursor: 'p2' }); await drain()
    expect(h.state.snapshot.value?.version).toBe('1')
    expect(h.requests[2]!.query.cursor).toBe('p2')
    h.respond(2, { ...data(2), changes: Array.from({ length: 344 }, (_, n) => event(n + 257)) }); await second
    expect(h.state.snapshot.value?.changes).toHaveLength(600)
    expect(h.state.summaryCount.value).toBe(570)
    expect(h.scene.attacks).toHaveLength(30)
    h.stop()
  })
  test('a version conflict retains the last coherent snapshot and schedules recovery', async () => {
    const h = harness(), first = h.state.refresh(); h.respond(0, data(1)); await first
    const second = h.state.refresh(); h.respond(1, { ...data(2), nextCursor: 'p2' }); await drain()
    h.respond(2, undefined, 409); await second
    expect(h.state.snapshot.value?.version).toBe('1')
    expect(h.timers.size).toBe(1)
    h.stop()
  })
  test('revocation midway through history clears the entire visible snapshot', async () => {
    const h = harness(), first = h.state.refresh(); h.respond(0, data(1)); await first
    const second = h.state.refresh(); h.respond(1, { ...data(2), nextCursor: 'p2' }); await drain()
    h.respond(2, undefined, 403); await second; await nextTick()
    expect(h.state.snapshot.value).toBeNull()
    expect(h.scene.states.at(-1)!.nodes).toEqual([])
    h.stop()
  })
  test('changing the time window and unmounting reject in-flight old results', async () => {
    const h = harness(), first = h.state.refresh()
    h.state.minutes.value = '30'; await nextTick()
    h.respond(0, data(1)); await first
    expect(h.state.snapshot.value).toBeNull()
    h.timers.values().next().value!()
    h.stop()
    expect(h.requests[1]!.signal.aborted).toBe(true)
    h.respond(1, data(2)); await drain()
    expect(h.state.snapshot.value).toBeNull()
  })
})
