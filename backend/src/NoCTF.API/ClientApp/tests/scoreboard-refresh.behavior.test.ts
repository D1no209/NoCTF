import { sourceFile } from './support/feature-source'
import { expect, test } from 'bun:test'
import { computed, ref, shallowRef, watch, hasInjectionContext, inject, provide } from 'vue'
import { createTrailingRefresh } from '../app/lib/latest-page-refresh'
import * as coherence from '../app/utils/scoreboard-coherence'
import { normalizeScoreboardSnapshot } from '../app/utils/scoreboard-board'

// Execute the actual composable with controlled REST/Hub/lifecycle boundaries.
// No global module mocks: other tests retain the real SDK and Vue modules.
const source = await sourceFile(new URL('../app/composables/useScoreboardMatrix.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace(/export function /g, 'function ')

function harness() {
  let version = 1
  let settled = false
  let processing = false
  let calls = 0
  let pendingRead: Promise<void> | null = null
  let mount = () => {}
  let dispose = () => {}
  let tick = () => {}
  let handlers: Record<string, (value?: unknown) => void> = {}
  const requests: Array<number | null> = []
  const deps = {
    ref, shallowRef, computed, watch, hasInjectionContext, inject, provide, createTrailingRefresh, ...coherence,
    normalizeScoreboardSnapshot,
    useAuth: () => ({ user: ref(null) }),
    translate: (value: string) => value,
    parseApiError: (value: unknown) => ({ message: String(value) }),
    competitionHubString: (value: Record<string, unknown>, key: string) => value[key],
    onMounted: (fn: () => void) => { mount = fn },
    onBeforeUnmount: (fn: () => void) => { dispose = fn },
    watchCompetition: (_: string, value: typeof handlers) => { handlers = value; return () => { handlers = {} } },
    setInterval: (fn: () => void) => { tick = fn; return 1 },
    clearInterval: () => { tick = () => {} },
    setTimeout: () => 1,
    clearTimeout: () => {},
    getScoreboardChallengeCatalogEndpoint: async () => ({ data: { revision: 'catalog', items: [{ id: 'challenge' }] } }),
    // The leaderboard carries its own rounds and challenge columns; no schema request exists.
    getLeaderboardEndpoint: async (request: { query: { endingRound: number | null } }) => {
      calls++
      requests.push(request.query.endingRound)
      if (pendingRead) await pendingRead
      return processing ? { response: { status: 202 } } : { data: {
        mode: 'Awdp', version: String(version), challengeCatalogRevision: 'catalog', dataScope: 'Live', currentRoundId: 'round',
        rounds: [{ id: 'round', number: version, state: settled ? 'Settled' : 'Running', endAt: `2026-09-08T00:0${version}:00Z` }],
        challenges: [{ competitionChallengeId: 'challenge', currentScore: null }],
        roundWindowStart: 1, roundWindowEnd: version, latestRound: version,
        teams: [{ teamId: 'team', totalScore: 100, slots: [{ challenge: 0, round: 0, netPoints: 100 }] }],
      } }
    },
  }
  const board = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useScoreboardMatrix('competition');`)(deps)
  return {
    holdRead: () => {
      let release!: () => void
      pendingRead = new Promise<void>(resolve => { release = resolve })
      return () => { pendingRead = null; release() }
    },
    board, mount: () => mount(), dispose: () => dispose(), tick: () => tick(),
    notice: () => handlers.scoreboardUpdated?.({ competitionId: 'competition', version: String(version), challengeCatalogRevision: 'catalog' }),
    update: (complete = false) => { version++; settled = complete },
    processing: (value: boolean) => { processing = value },
    calls: () => calls, requests,
  }
}

test('notice covered by an active response does not fetch the same publication again', async () => {
  const app = harness()
  app.mount(); await drain()
  app.update()
  const release = app.holdRead()
  const reading = app.board.refresh()
  app.notice()
  await drain(550)
  expect(app.calls()).toBe(2)
  release(); await reading; await drain(550)
  expect(app.board.snapshot.value.version).toBe('2')
  expect(app.calls()).toBe(2)
  app.dispose()
})

async function drain(milliseconds = 0) { await new Promise(resolve => setTimeout(resolve, milliseconds)) }

test('duration update notification replaces rounds and slots in one leaderboard read without a page reload', async () => {
  const app = harness()
  app.mount(); await drain()
  expect(app.board.schema.value.rounds[0].endAt).toBe('2026-09-08T00:01:00Z')
  const reads = app.calls()
  app.update(); app.notice(); await drain(550)
  expect(app.calls()).toBe(reads + 1)
  expect(app.board.schema.value.rounds[0].endAt).toBe('2026-09-08T00:02:00Z')
  expect(app.board.schema.value.columns).toEqual([{ index: 0, competitionChallengeId: 'challenge', roundId: 'round' }])
  expect(app.board.snapshot.value.teams[0].slots[0]).toMatchObject({ columnIndex: 0, competitionChallengeId: 'challenge', roundId: 'round' })
  app.dispose()
})

test('terminal refresh retains rounds and cells, then stops periodic REST reads', async () => {
  const app = harness()
  app.mount(); await drain()
  app.update(true); app.tick(); await drain()
  expect(app.board.schema.value.rounds[0].state).toBe('Settled')
  expect(app.board.snapshot.value.teams[0].slots).toHaveLength(1)
  const calls = app.calls()
  app.tick(); await drain()
  expect(app.calls()).toBe(calls)
  app.dispose()
})

test('processing refresh preserves the last coherent board and recovers on the next fetch', async () => {
  const app = harness()
  app.mount(); await drain()
  app.update(); app.processing(true); app.notice(); await drain(550)
  expect(app.board.snapshot.value.version).toBe('1')
  expect(app.board.schema.value.rounds[0].number).toBe(1)
  expect(app.board.processing.value).toBeTrue()
  app.processing(false); await app.board.refresh()
  expect(app.board.snapshot.value.version).toBe('2')
  expect(app.board.processing.value).toBeFalse()
  app.dispose()
  const calls = app.calls()
  app.tick(); app.notice(); await drain()
  expect(app.calls()).toBe(calls)
})
