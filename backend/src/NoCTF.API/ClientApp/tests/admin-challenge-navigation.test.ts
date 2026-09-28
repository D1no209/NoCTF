import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

const page = () => sourceFile(new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url)).text()

describe('admin competition challenge navigation', () => {
  test('places the challenge sections in the shared right sidebar', async () => {
    const source = await page()
    const css = await sourceFile(new URL('../app/components/views/app/settings-workspace.css', import.meta.url)).text()

    expect(source).toContain('<ChoiceSidebar')
    expect(source).toContain('v-model="activeSection"')
    expect(source).toContain('data-side="right"')
    expect(source).toContain(':items="sectionOptions"')
    expect(source).toContain('controls="competition-challenge-editor-content"')
    expect(source).not.toContain('<TabsList')
    expect(source).not.toContain('<TabsTrigger')
    expect(css).toContain('[data-challenge-editor-workspace] > [data-side=\'right\'] { grid-column: 2; grid-row: 1; }')
    expect(css).toContain('transform-origin: right center;')
  })

  test('paginates and searches challenge team scoring before allowing a judge to correct scoring', async () => {
    const source = await page()

    expect(source).toContain('adminListTeams')
    expect(source).toContain('useOffsetPagination<NoCtfapiEndpointsTeamsTeamResponse>')
    expect(source).toContain('keyword: scoringSearch.value.trim() || null, offset, limit, desc')
    expect(source).toContain('watch(scoringSearch, reloadScoringTeamsFromFirstPage)')
    expect(source).toContain('setTimeout(() => { void scoringPagination.loadPage(1) }, 250)')
    expect(source).toContain('v-model="scoringSearch"')
    expect(source).toContain('<OffsetPagination')
    expect(source).toContain('@update:page="loadScoringPage"')
    expect(source).not.toContain('query: { keyword: null, offset: 0, limit: 200, desc: false }')
    expect(source).toContain('adminListGameplayFacts')
    expect(source).toContain('getLeaderboardEndpoint')
    expect(source).toContain('getScoreboardSchemaEndpoint')
    expect(source).toContain('do {')
    expect(source).toContain('} while (offset < total && offset > 0)')
    expect(source).toContain('query: { competitionChallengeId: ccId, offset, limit: 200, desc: true }')
    expect(source).toContain('v-for="row in scoringRows"')
    expect(source).toContain('v-if="canJudge"')
    expect(source).toContain('adminCreateManualAdjustment')
    expect(source).toContain('body: { teamId, competitionChallengeId: ccId, delta: adjustmentDelta.value }')
    expect(source).toContain('if (!teamId || !adjustmentValid.value || adjustmentPending.value) return')

    const catchStart = source.indexOf('catch (requestError)', source.indexOf('async function submitAdjustment'))
    const finallyStart = source.indexOf('finally', catchStart)
    const catchBody = source.slice(catchStart, finallyStart)
    expect(catchBody).not.toContain('adjustmentTarget.value = null')
    expect(catchBody).not.toContain('adjustmentDelta.value = 0')
  })
})

describe('challenge template list navigation', () => {
  test('enforces organizer access at the route boundary', async () => {
    const [indexRoute, newRoute, detailRoute, middleware, trafficCaptures] = await Promise.all([
      Bun.file(new URL('../app/pages/admin/challenges/index.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/pages/admin/challenges/new.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/pages/admin/challenges/[id].vue', import.meta.url)).text(),
      Bun.file(new URL('../app/middleware/organizer.ts', import.meta.url)).text(),
      Bun.file(new URL('../app/pages/admin/competitions/[id]/traffic-captures.vue', import.meta.url)).text(),
    ])

    for (const route of [indexRoute, newRoute, detailRoute])
      expect(route).toContain("middleware: 'organizer'")
    expect(middleware).toContain('canOrganize')
    expect(middleware).toContain("path: '/auth/login'")
    expect(trafficCaptures).toContain("middleware: 'platform-admin'")
  })

  test('uses server-side offset pagination and debounced filters', async () => {
    const source = await sourceFile(
      new URL('../app/pages/admin/challenges/index.vue', import.meta.url),
    ).text()

    expect(source).toContain('useOffsetPagination<ChallengeTemplate>')
    expect(source).toContain('offset,')
    expect(source).toContain('keyword: search.value.trim() || null')
    expect(source).toContain('setTimeout(() => { void load() }, 250)')
    expect(source).toContain('v-if="loading && templates.length === 0"')
  })

  test('creates templates in a modal with a bounded direction selector', async () => {
    const index = await sourceFile(
      new URL('../app/pages/admin/challenges/index.vue', import.meta.url),
    ).text()
    const route = await Bun.file(
      new URL('../app/pages/admin/challenges/new.vue', import.meta.url),
    ).text()
    const dialog = await sourceFile(
      new URL('../app/features/admin/ChallengeTemplateCreateDialog.vue', import.meta.url),
    ).text()

    expect(index).toContain('@click="setCreateOpen(true)"')
    expect(index).toContain(':is="ChallengeTemplateCreateDialog"')
    expect(index).not.toContain('to="/admin/challenges/new"')
    expect(route).toContain("AdminChallengesIndexPage.vue")
    expect(dialog).toContain('<Dialog :open="open"')
    expect(dialog).toContain('<Select v-model="direction">')
    expect(dialog).toContain('v-for="option in directionOptions"')
    expect(dialog).toContain('challengeDirectionOptions')
    expect(dialog).not.toMatch(/<Input[\s\S]{0,160}v-model="direction"/)
    expect(dialog).toContain('class="px-1"')
    expect(dialog).toContain('overscroll-contain')
  })

  test('filters the challenge library by direction', async () => {
    const index = await sourceFile(
      new URL('../app/pages/admin/challenges/index.vue', import.meta.url),
    ).text()

    expect(index).toContain("const directionFilter = ref(typeof route.query.direction === 'string'")
    expect(index).toContain('const filteredTemplates = computed(')
    expect(index).toContain('v-model="directionFilter"')
    expect(index).toContain("$t('ui.allDirections')")
    expect(index).toContain('v-for="template in filteredTemplates"')
    expect(index).toContain('interface ChallengeLibrarySnapshot')
    expect(index).toContain("const directionFilter = ref(typeof route.query.direction === 'string'")
    expect(index).toContain("const search = ref(typeof route.query.q === 'string'")
    expect(index).toContain('templates: [...templates.value]')
    expect(index).toContain('directions: [...directions.value]')
    expect(index).toContain('directionCatalogs.set(includeDeleted.value, catalog)')
    expect(index).toContain('if (!pagination.error.value) rememberSnapshot()')
    expect(index).toContain('function syncFiltersToRoute(): void')
    expect(index).toContain("query.direction = directionFilter.value")
    expect(index).toContain("query.deleted = '1'")
    expect(index).toContain('void router.replace({ query })')
    expect(index).toContain('onBeforeUnmount(() => {')
    expect(index).toContain('rememberSnapshot()')
  })
})
