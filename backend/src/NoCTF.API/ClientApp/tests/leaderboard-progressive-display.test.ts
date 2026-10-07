import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { horizontalScrollbarPinPosition } from '../app/components/ui/scroll-area/scrollbars'

const page = await sourceFile(
  new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url),
).text()
const scoreboard = await sourceFile(
  new URL('../app/utils/scoreboard.ts', import.meta.url),
).text()
const slotStatus = await sourceFile(
  new URL('../app/features/leaderboard/ScoreboardSlotStatus.vue', import.meta.url),
).text()
const teamDetail = await sourceFile(
  new URL('../app/features/leaderboard/ScoreboardTeamDetailDialog.vue', import.meta.url),
).text()
const trendChart = await sourceFile(
  new URL('../app/features/leaderboard/ScoreTrendChart.vue', import.meta.url),
).text()
const trendChartView = await sourceFile(
  new URL('../app/components/views/leaderboard/ScoreTrendChartView.vue', import.meta.url),
).text()
const theme = await sourceFile(
  new URL('../app/assets/css/main.css', import.meta.url),
).text()

describe('leaderboard progressive display', () => {
  test('keeps the full snapshot and progressively reveals stable entries', () => {
    expect(page).toContain('const visibleTeamCount = ref(50)')
    expect(page).toContain('filteredTeams.value.slice(0, visibleTeamCount.value)')
    expect(page).toContain('v-for="(team, teamIndex) in visibleTeams"')
    expect(page).toContain('visibleTeams.length < filteredTeams.length')
    expect(page).toContain('function showMoreTeams(): void')
    expect(page).toContain('@click="showMoreTeams"')
  })

  test('does not recreate numbered client-side pagination', () => {
    expect(page).not.toContain('const pageSize =')
    expect(page).not.toContain('const totalPages =')
    expect(page).not.toContain('@click="page--"')
    expect(page).not.toContain('@click="page++"')
  })

  test('keeps identity columns frozen while every matrix column remains readable', () => {
    expect(page).toContain('<Table pin-horizontal-scrollbar class="min-w-max table-auto">')
    expect(page).toContain('sticky left-0 z-30 w-20 min-w-20 max-w-20')
    expect(page).toContain('sticky left-20 z-30 w-56 min-w-56 max-w-56')
    expect(page).toContain('sticky left-76 z-30 w-28 min-w-28 max-w-28')
    expect(page).toContain(':content="displayTeamName(team)"')
    expect(page).toContain('w-full whitespace-normal break-words')
    expect(page).not.toContain('min-w-0 flex-1 truncate')
  })

  test('keeps AWDP scores in authoritative detail while matrix cells show status icons', async () => {
    expect(scoreboard).toContain("scoreboardActivity(slot, ['Attack'])")
    expect(scoreboard).toContain("scoreboardActivity(slot, ['Defense'])")
    expect(slotStatus).toContain('<Flag')
    expect(slotStatus).toContain('<ShieldCheck')
    expect(page).toContain("<component :is=\"ScoreboardSlotStatus\"")
    expect(page).toContain("slot?.scoreState === 'Settled'")
    expect(page).toContain("slot?.scoreState === 'Settled' ? slot.netPoints ?? 0 : ''")
    expect(page).toContain('getScoreboardSlotDetailEndpoint')
    expect(page).toContain('getScoreboardAdjustmentDetailEndpoint')
    expect(page).toContain('(team.globalAdjustmentCount ?? 0) > 0')
    expect(page).toContain("leaderboard.competitionsBy.description.completeAdjustmentHistoryComes")
    expect(page).toContain("leaderboard.competitionsBy.description.scoresStatesComeAuthoritative")
    expect(page).toContain("isCtf ? $t('leaderboard.competitionsBy.description.scoresComeAuthoritativeServer') : $t('leaderboard.competitionsBy.description.scoresStatesComeAuthoritative')")
    expect(page).toContain(":class=\"isCtf ? 'grid-cols-2' : 'grid-cols-3'\"")
    expect(page).toContain('<div v-if="!isCtf" class="rounded-lg border p-3">')
    expect(page).not.toContain('entry.attackScore')
    expect(page).not.toContain('entry.defenseScore')
  })

  test('keeps the overall CTF trend above the matrix and moves the team trend into team detail', () => {
    expect(page).toContain('getLeaderboardTrendsEndpoint')
    expect(page).toContain("const visibleTrendSeries = computed<TrendSeries[]>")
    expect(page).toContain("teams.value")
    expect(page).not.toContain("$t('common.label.overallScoreTrend')")
    expect(page).not.toContain("$t('common.description.cumulativeScoreChangesTeams')")
    expect(page).toContain('<component :is="LazyScoreTrendChart" v-else :series="visibleTrendSeries"')
    expect(page).toContain("<component :is=\"LazyScoreboardTeamDetailDialog\"")
    expect(page).toContain(':trend-series="teamDetailTrendSeries"')
    expect(page).not.toContain('selectedTrendTeamId')
    expect(page).not.toContain('selectedTrendSeries')
    expect(teamDetail).toContain("$t('leaderboard.label.teamScoreTrend')")
    expect(teamDetail).toContain("<component :is=\"LazyScoreTrendChart\"")
    expect(teamDetail.indexOf('scoreboard-team-trend-title'))
      .toBeLessThan(teamDetail.indexOf('scoreboard-team-radar-title'))
  })

  test('uses an internal scroll surface while the overall trend remains in normal flow', () => {
    expect(page).toContain('data-scoreboard-page-scroll')
    expect(page).toContain('<ScrollSurface as="div" axis="y" data-scoreboard-page-scroll')
    expect(page).toContain('min-h-0 flex-1 overflow-y-auto overscroll-contain')
    expect(page).toContain('@wheel.capture="dampenLeaderboardWheel"')
    expect(page).toContain('leaderboardWheelDamping = 0.55')
    expect(page).toContain('surface.scrollTop = next')
    expect(page).toContain('data-slot="leaderboard-trend-panel"')
    expect(page).not.toContain('md:sticky md:top-24 md:z-20')
    expect(theme).not.toContain(':has(> main [data-contained-workspace-page] > [data-scoreboard-page-scroll])')
  })

  test('returns from the leaderboard to the playable challenge workspace', () => {
    expect(page).toContain('const competitionReturnPath = competitionChallengesPath(competitionId)')
    expect(page).toContain(':to="competitionReturnPath"')
    expect(page).not.toContain(':to="`/competitions/${competitionId}`"')
  })

  test('lets vertical wheel input escape the horizontal score matrix', () => {
    expect(theme).toContain("[data-scroll-surface][data-scroll-axis='x']")
    expect(theme).toContain('overscroll-behavior-x: contain;')
    expect(theme).toContain('overscroll-behavior-y: auto;')
  })

  test('pins the horizontal scrollbar to the visible bottom of a long score matrix', () => {
    expect(page).toContain('<Table pin-horizontal-scrollbar')
    expect(horizontalScrollbarPinPosition(
      { top: 420, right: 1880, bottom: 1600, left: 60 },
      { top: 290, right: 1900, bottom: 1010, left: 40 },
      { top: 180, right: 1920, bottom: 1050, left: 20 },
      8,
    )).toEqual({ top: 822, left: 40, width: 1820 })
    expect(horizontalScrollbarPinPosition(
      { top: 1100, right: 1880, bottom: 1600, left: 60 },
      { top: 290, right: 1900, bottom: 1010, left: 40 },
      { top: 180, right: 1920, bottom: 1050, left: 20 },
      8,
    )).toBeNull()
  })

  test('keeps score detail dialogs on the opaque theme surface', () => {
    expect(theme).toContain(".card-surface.card-surface:is([data-slot='dialog-content'], [data-slot='alert-dialog-content'])")
    expect(theme).toContain('background: var(--popover);')
  })

  test('keeps every team visible on hover and assigns series colors from a diverse theme palette', () => {
    expect(trendChart).toContain("colorBy: 'series' as const")
    expect(trendChart).toContain('hoverLink: true')
    expect(trendChart).toContain("selectedMode: 'multiple'")
    expect(trendChart).toContain("focus: 'series' as const")
    expect(trendChart).toContain("chart.on('legendselectchanged', toggleTeamFocus)")
    expect(trendChart).toContain('focusedTeamName === name ? null : name')
    expect(trendChart).toContain('const palette = trendChartPalette(el.value)')
    expect(trendChart).toContain('shadowBlur: focusedTeamName ? 16 : 0')
    expect(trendChart).toContain("shadowColor: focusedTeamName ? lineColor : 'transparent'")
    expect(trendChart).toContain('shadowBlur: 18')
    expect(trendChart).toContain("appendTo: 'body'")
    expect(trendChart).toContain("className: 'noctf-chart-tooltip'")
    expect(trendChart).toContain('width === observedWidth && height === observedHeight')
    expect(trendChart).toContain('resizeFrame = requestAnimationFrame')
    expect(trendChart).toContain('new Map((team.points ?? [])')
    expect(trendChart).toContain('props.revision')
    expect(trendChart).not.toContain('{ deep: true }')
    expect(trendChart).toContain('scheduleRender')
    expect(trendChart).toContain('renderFrame = requestAnimationFrame')
    expect(trendChart).toContain('notMerge: false')
    expect(trendChart).toContain("replaceMerge: ['series']")
    expect(trendChart).toContain('min: timeRange.axisMin')
    expect(trendChart).toContain('max: timeRange.axisMax')
    expect(trendChart).toContain("type: 'slider'")
    expect(trendChart.match(/filterMode: 'none'/g)).toHaveLength(4)
    expect(trendChart).toContain("chart.on('datazoom', rememberZoom)")
    expect(trendChart).toContain("id: 'score-trend-score-inside'")
    expect(trendChart).toContain("zoomOnMouseWheel: 'ctrl'")
    expect(trendChart).toContain('selectedScoreRange')
    expect(theme.match(/--chart-(?:[1-9]|10): oklch\([^)]+\);/g)).toHaveLength(20)
    expect(theme.match(/--trend-chart-(?:[1-9]|10): oklch\([^)]+\);/g)).toHaveLength(20)
  })

  test('lets ordinary wheel input reach the leaderboard scroll surface while retaining ctrl zoom', () => {
    expect(trendChartView).toContain('@wheel.capture="releaseWheelToScrollSurface"')
    expect(trendChart).toContain('if (!event.ctrlKey) event.stopImmediatePropagation()')
    expect(trendChart).toContain("zoomOnMouseWheel: 'ctrl'")
  })
})
