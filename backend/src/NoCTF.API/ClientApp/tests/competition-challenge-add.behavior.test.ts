import { describe, expect, test } from 'bun:test'
import { computed, effectScope, proxyRefs, reactive, ref, toRefs, watch } from 'vue'
import { nextCompetitionChallengeOrder } from '../app/lib/competition-challenge-order'
import { availablePlacementCompetitions, placementManagementPath, projectChallengePlacements, writablePlacementCompetitions } from '../app/features/admin/challenge-competition-placements'

const competition = { id: 'competition', mode: 'Ctf' as const, administrationRole: 'Owner' as const }
const rows = [
  { id: 'active', challengeId: 'other', order: 72 },
  { id: 'deleted-73', challengeId: 'deleted-template', order: 73, deletedAt: '2026-10-10' },
  { id: 'deleted-74', challengeId: 'another-template', order: 74, deletedAt: '2026-10-10' },
]

async function harness(relativePath: string, functionName: string, props?: unknown) {
  const source = await Bun.file(new URL(relativePath, import.meta.url)).text()
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '').replace(/export function /g, 'function ')
  const reads: any[] = []
  const writes: any[] = []
  const mounted: Array<() => Promise<void>> = []
  const deps = {
    computed, ref, watch, toRefs, proxyRefs, Plus: {}, ArrowUpRight: {}, ChevronLeft: {}, ChevronRight: {}, RefreshCw: {},
    onMounted: (callback: () => Promise<void>) => mounted.push(callback), onBeforeUnmount: () => {},
    useCompetitionAdmin: () => ({ competitionId: competition.id, competition: ref(competition), canWrite: ref(true) }),
    useAuth: () => ({ user: ref({ userId: 'actor' }) }),
    nextCompetitionChallengeOrder, availablePlacementCompetitions, placementManagementPath, projectChallengePlacements, writablePlacementCompetitions,
    adminListCompetitions: async () => ({ data: { items: [competition] } }),
    adminListCompetitionChallenges: async (options: any) => {
      reads.push(options)
      return { data: { items: options.query.includeDeleted ? rows : rows.filter(row => !row.deletedAt) } }
    },
    adminChallengeBankListTemplates: async () => ({ data: { items: [{ id: 'new-template', mode: 'Ctf' }] } }),
    adminCreateCompetitionChallenge: async (options: any) => { writes.push(options); return {} },
    challengeTagOptions: () => [], describeMessage: (key: string) => key,
    parseApiError: () => ({ displayMessage: 'failed' }), competitionChallengeConflictMessage: () => undefined,
    toast: { success: () => {} },
  }
  const factory = new Function('deps', `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return ${functionName};`)(deps)
  const scope = effectScope()
  const state = scope.run(() => factory(props ? reactive(props as object) : undefined))!
  return { state, reads, writes, mount: () => Promise.all(mounted.map(callback => callback())), stop: () => scope.stop() }
}

describe('adding competition challenges after soft deletion', () => {
  test('manual add reserves deleted positions while the list hides deleted challenges', async () => {
    const app = await harness('../app/features/routes/admin/competitions/[id]/challenges/useAdminCompetitionsByIdChallengesIndexPage.ts', 'useAdminCompetitionsByIdChallengesIndexPage')
    try {
      await app.mount()
      expect(app.state.items.value.map((row: any) => row.id)).toEqual(['active'])
      await app.state.openAdd()
      expect(app.reads.at(-1).query.includeDeleted).toBe(true)
      expect(app.state.newOrder.value).toBe(75)
      app.state.selectedTemplateId.value = 'new-template'
      await app.state.addChallenge()
      expect(app.writes[0].body.order).toBe(75)
      expect(app.state.addOpen.value).toBe(false)
    } finally { app.stop() }
  })

  test('question bank placement reserves deleted positions without displaying them as active placements', async () => {
    const app = await harness('../app/features/admin/useChallengeCompetitionPlacements.ts', 'useChallengeCompetitionPlacements',
      { challengeId: 'new-template', mode: 'Ctf', disabled: false })
    try {
      await app.state.load()
      expect(app.reads[0].query.includeDeleted).toBe(true)
      expect(app.state.linked.value).toHaveLength(0)
      expect(app.state.preview.value.nextOrder).toBe(75)
      await app.state.addToCompetition()
      expect(app.writes[0].body.order).toBe(75)
    } finally { app.stop() }
  })
})
