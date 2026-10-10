import { describe, expect, test } from 'bun:test'
import { computed, effectScope, markRaw, nextTick, proxyRefs, reactive, ref, watch } from 'vue'
import { createTrailingRefresh } from '../app/lib/latest-page-refresh'

const compile = async (path: string, name: string) => {
  const source = await Bun.file(new URL(path, import.meta.url)).text()
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(source)
    .replace(/^import[\s\S]*?from ["'][^"']+["'];?\s*$/gm, '')
    .replace(/export function /g, 'function ')
  return (deps: Record<string, unknown>) => new Function('deps',
    `const { ${Object.keys(deps).join(', ')} } = deps; ${compiled}; return ${name};`)(deps)
}
const shellFactory = await compile('../app/features/routes/admin/competitions/useAdminCompetitionsByIdPage.ts', 'useAdminCompetitionsByIdPage')
const reviewFactory = await compile('../app/features/routes/competitions/[id]/useCompetitionsByIdWriteUpsPage.ts', 'useCompetitionsByIdWriteUpsPage')
const drain = async () => { await nextTick(); await new Promise(resolve => setTimeout(resolve, 0)) }

test('management review remains discoverable for finished competitions and uses the contained wide workspace', () => {
  const route = reactive({ path: '/admin/competitions/competition/writeups', params: { id: 'competition' } })
  const icons = Object.fromEntries('Activity ChartNoAxesCombined ClipboardCheck Container Download FileCheck GitBranch KeyRound LayoutDashboard Mail Network Orbit Puzzle Settings ShieldAlert Trophy Users Webhook'.split(' ').map(name => [name, {}]))
  const deps = { ...icons, computed, ref, proxyRefs, markRaw, useRoute: () => route,
    useAuth: () => ({ user: ref(null), isAdministrator: ref(true) }),
    translate: (key: string) => key, provide: () => {}, onMounted: () => {},
    adminWorkspacePath: (path: string) => path, CompetitionAdminKey: Symbol(),
    CompetitionStatusBadgeComponent: {}, GameModeBadgeComponent: {}, AppWorkspaceNavComponent: {} }
  const state = shellFactory(deps)()
  for (const status of ['Draft', 'Running', 'Finished']) {
    state.competition.value = { id: 'competition', title: 'Competition', mode: 'Ctf', status }
    const link = state.navGroups.value.flatMap((group: { items: Array<{ to: string; label: string }> }) => group.items)
      .find((item: { to: string }) => item.to === route.path)
    expect(link?.label).toBe('writeUp.review')
    expect(state.isWriteUpReview.value).toBeTrue()
    expect(state.usesPageScroll.value).toBeFalse()
  }
  route.path = '/admin/competitions/competition/teams'
  expect(state.isWriteUpReview.value).toBeFalse()
  expect(state.usesPageScroll.value).toBeTrue()
})

function harness(canJudge: boolean) {
  const route = reactive({ path: '/admin/competitions/competition/writeups', params: { id: 'competition' }, query: { team: 'team-b', filter: 'retained' } })
  const review = { scoreboardAvailable: true, canJudge, items: ['team-a', 'team-b'].map(teamId => ({
    writeUp: { teamId, teamName: teamId, fileId: `file-${teamId}`, fileName: 'writeup.pdf' },
    originalTotalScore: 100, originalRank: 2, adjustedTotalScore: 100, adjustedRank: 2,
    challengeScores: [{ competitionChallengeId: 'challenge', title: 'Challenge', direction: 'Web', netPoints: 100 }],
  })) }
  const reads: string[] = []
  const previews: string[] = []
  const writes: unknown[] = []
  const scope = effectScope()
  let observe!: () => Promise<boolean>
  const polling = ref(false)
  const deps = {
    ref, computed, watch, createTrailingRefresh, markRaw, ChallengeWriteUpReviewComponent: {},
    ...Object.fromEntries('ArrowLeft Download FileSearch MessageCircleQuestion MinusCircle RefreshCw Scale'.split(' ').map(name => [name, {}])),
    useRoute: () => route,
    useRouter: () => ({ replace: async (next: { query: typeof route.query }) => { route.query = next.query }, push: async () => {} }),
    onMounted: () => {}, onUnmounted: () => {},
    usePolling: (callback: () => Promise<boolean>) => { observe = callback; return { polling, timedOut: ref(false), start: () => { polling.value = true } } },
    listTeamWriteUps: async ({ path }: { path: { competitionId: string } }) => { reads.push(path.competitionId); return { data: structuredClone(review) } },
    issueTeamWriteUpPreview: async ({ path }: { path: { teamId: string } }) => { previews.push(path.teamId); return { data: { previewUrl: `/preview/${path.teamId}` } } },
    adminCreateManualAdjustment: async (request: unknown) => { writes.push(request); return { data: { gameplayFactId: 'adjustment' } } },
    translate: (key: string) => key, describeMessage: (key: string) => ({ key }),
    parseApiError: () => ({ displayMessage: 'failed' }),
    toast: { success: () => {}, error: () => {} },
  }
  const state = scope.run(() => reviewFactory(deps)({ management: true }))!
  return { state, route, review, reads, previews, writes, observe: () => observe(), stop: () => scope.stop() }
}

describe('management WriteUp review', () => {
  test('opens the deep-linked team, retains its admin route and observes authoritative score changes', async () => {
    const app = harness(true)
    try {
      await app.state.load()
      await drain()
      expect(app.reads).toEqual(['competition'])
      expect(app.state.management).toBeTrue()
      expect(app.state.selected.value.writeUp.teamId).toBe('team-b')
      expect(app.state.previewUrl.value).toBe('/preview/team-b')
      expect(app.state.canJudge.value).toBeTrue()
      await app.state.selectTeam('team-a')
      await drain()
      expect(app.route.path).toBe('/admin/competitions/competition/writeups')
      expect(app.route.query).toEqual({ team: 'team-a', filter: 'retained' })
      expect(app.previews).toEqual(['team-b', 'team-a'])

      app.state.adjustmentDelta.value = -40
      app.state.submitAdjustment()
      await drain()
      expect(app.writes).toEqual([{ path: { competitionId: 'competition' }, body: {
        teamId: 'team-a', competitionChallengeId: 'challenge', delta: -40,
      } }])
      app.review.items[0]!.challengeScores[0]!.netPoints = 60
      app.review.items[0]!.adjustedTotalScore = 60
      expect(await app.observe()).toBeTrue()
      expect(app.state.selected.value.adjustedTotalScore).toBe(60)
      expect(app.state.selected.value.challengeScores[0].netPoints).toBe(60)
    }
    finally { app.stop() }
  })

  test('management placement does not grant a read-only reviewer scoring or consultation rights', async () => {
    const app = harness(false)
    try {
      await app.state.load()
      await drain()
      expect(app.state.previewUrl.value).toBe('/preview/team-b')
      expect(app.state.canJudge.value).toBeFalse()
      app.state.adjustmentDelta.value = -40
      app.state.submitAdjustment()
      app.state.openConsultation()
      await drain()
      expect(app.writes).toEqual([])
      expect(app.state.consultationOpen.value).toBeFalse()
    }
    finally { app.stop() }
  })
})

test('the platform-admin management route composes the existing PDF review and fills its transition frame', async () => {
  const page = await Bun.file(new URL('../app/pages/admin/competitions/[id]/writeups.vue', import.meta.url)).text()
  const feature = await Bun.file(new URL('../app/features/routes/admin/competitions/[id]/AdminCompetitionsByIdWriteUpsPage.vue', import.meta.url)).text()
  const shell = await Bun.file(new URL('../app/components/views/page/admin/competitions/AdminCompetitionsByIdPageView.vue', import.meta.url)).text()
  const css = await Bun.file(new URL('../app/components/views/app/settings-workspace.css', import.meta.url)).text()
  expect(page).toContain("middleware: 'platform-admin'")
  expect(feature).toContain('useCompetitionsByIdWriteUpsPage({ management: true })')
  expect(feature).toContain('CompetitionsByIdWriteUpsPageView.vue')
  expect(shell).toContain("isProgressionPage || isWriteUpReview ? 'max-w-none'")
  expect(shell).toContain('data-admin-writeup-review-workspace')
  expect(css).toContain('[data-admin-writeup-review-workspace] > .noctf-motion-viewport > div')
})
