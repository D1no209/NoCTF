import { createTestApi, kiotaBindings } from './support/kiota-harness'
import { sourceFile } from './support/feature-source'
import { expect, test } from 'bun:test'
import { computed, ref } from 'vue'
import { createTrailingRefresh } from '../app/lib/latest-page-refresh'
import * as coherence from '../app/utils/scoreboard-coherence'

// Execute the actual composable with controlled REST/Hub/lifecycle boundaries.
// No global module mocks: other tests retain the real SDK and Vue modules.
const source = await sourceFile(new URL('../app/composables/useScoreboardMatrix.ts', import.meta.url)).text()
const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
  .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
  .replace('export function useScoreboardMatrix', 'function useScoreboardMatrix')

function harness() {
  let version = 1
  let revision = 10
  let settled = false
  let processing = false
  let calls = 0
  let mount = () => {}
  let dispose = () => {}
  let tick = () => {}
  let handlers: Record<string, (value?: unknown) => void> = {}
  const requests: Array<number | null> = []
  const deps = { ...kiotaBindings(source), api: createTestApi({ 'GET /api/v1/competitions/{competitionId}/leaderboard/challenges': async () => ({ revision: 'catalog', items: [{ id: 'challenge' }] }),
'GET /api/v1/competitions/{competitionId}/leaderboard/schema': async () => ({
      mode: 'Awdp', revision: String(revision), challengeCatalogRevision: 'catalog',
      rounds: [{ id: 'round', number: version, state: settled ? 'Settled' : 'Running', endAt: `2026-09-08T00:0${version}:00Z` }],
      columns: [{ index: 0, competitionChallengeId: 'challenge', roundId: 'round' }],
      roundWindowStart: 1, roundWindowEnd: version, latestRound: version,
    }),
'GET /api/v1/competitions/{competitionId}/leaderboard': async (request: { query: { endingRound: number | null } }) => {
      calls++
      requests.push(request.query.endingRound)
      return processing ? new Response(null, { status: 202 }) : {
        version: String(version), schemaRevision: String(revision), dataScope: 'Live', currentRoundId: 'round',
        teams: [{ teamId: 'team', totalScore: 100, slots: [{ columnIndex: 0, netPoints: 100 }] }],
      }
    } }),
ref,
computed,
createTrailingRefresh,
...coherence,
translate: (value: string) => value,
parseApiError: (value: unknown) => ({ message: String(value) }),
competitionHubString: (value: Record<string, unknown>, key: string) => value[key],
onMounted: (fn: () => void) => { mount = fn },
onBeforeUnmount: (fn: () => void) => { dispose = fn },
watchCompetition: (_: string, value: typeof handlers) => { handlers = value; return () => { handlers = {} } },
setInterval: (fn: () => void) => { tick = fn; return 1 },
clearInterval: () => { tick = () => {} },
setTimeout: () => 1,
clearTimeout: () => {} }
  const board = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return useScoreboardMatrix('competition');`)(deps)
  return {
    board, mount: () => mount(), dispose: () => dispose(), tick: () => tick(),
    notice: () => handlers.scoreboardUpdated?.({ competitionId: 'competition', version: String(version), schemaRevision: String(revision), challengeCatalogRevision: 'catalog' }),
    update: (complete = false) => { version++; revision++; settled = complete },
    processing: (value: boolean) => { processing = value },
    calls: () => calls, requests,
  }
}

async function drain() { await new Promise(resolve => setTimeout(resolve, 0)) }

test('duration update notification replaces schema and snapshot together without a page reload', async () => {
  const app = harness()
  app.mount(); await drain()
  expect(app.board.schema.value.rounds[0].endAt.toISOString()).toBe('2026-09-08T00:01:00.000Z')
  app.update(); app.notice(); await drain()
  expect(app.board.schema.value.rounds[0].endAt.toISOString()).toBe('2026-09-08T00:02:00.000Z')
  expect(app.board.snapshot.value.schemaRevision).toBe(app.board.schema.value.revision)
  expect(app.board.schema.value.columns).toHaveLength(1)
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
  app.update(); app.processing(true); app.notice(); await drain()
  expect(app.board.snapshot.value.version).toBe('1')
  expect(app.board.schema.value.revision).toBe('10')
  expect(app.board.processing.value).toBeTrue()
  app.processing(false); await app.board.refresh()
  expect(app.board.snapshot.value.version).toBe('2')
  expect(app.board.processing.value).toBeFalse()
  app.dispose()
  const calls = app.calls()
  app.tick(); app.notice(); await drain()
  expect(app.calls()).toBe(calls)
})
