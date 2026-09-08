import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

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
const theme = await sourceFile(
  new URL('../app/assets/css/main.css', import.meta.url),
).text()

describe('leaderboard progressive display', () => {
  test('keeps the full snapshot and progressively reveals stable entries', () => {
    expect(page).toContain('const visibleTeamCount = ref(50)')
    expect(page).toContain('teams.value.slice(0, visibleTeamCount.value)')
    expect(page).toContain('v-for="team in visibleTeams"')
    expect(page).toContain('visibleTeams.length < teams.length')
    expect(page).toContain('visibleTeamCount += 50')
  })

  test('does not recreate numbered client-side pagination', () => {
    expect(page).not.toContain('const pageSize =')
    expect(page).not.toContain('const totalPages =')
    expect(page).not.toContain('@click="page--"')
    expect(page).not.toContain('@click="page++"')
  })

  test('keeps identity columns frozen while every matrix column remains readable', () => {
    expect(page).toContain('<Table class="min-w-max table-auto">')
    expect(page).toContain('sticky left-0 z-30 w-20 min-w-20 max-w-20')
    expect(page).toContain('sticky left-20 z-30 w-56 min-w-56 max-w-56')
    expect(page).toContain('sticky left-76 z-30 w-28 min-w-28 max-w-28')
    expect(page).toContain(':title="displayTeamName(team)"')
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
    expect(page).toContain("ui.theCompleteAdjustmentHistoryComesFromAuthoritativeServerFacts")
    expect(page).toContain("ui.scoresAndStatesComeFromAuthoritativeServerSettlement")
    expect(page).toContain("isCtf ? $t('ui.scoresComeFromTheAuthoritativeServerSideScoringResult') : $t('ui.scoresAndStatesComeFromAuthoritativeServerSettlement')")
    expect(page).toContain(":class=\"isCtf ? 'grid-cols-2' : 'grid-cols-3'\"")
    expect(page).toContain('<div v-if="!isCtf" class="rounded-lg border p-3">')
    expect(page).not.toContain('entry.attackScore')
    expect(page).not.toContain('entry.defenseScore')
  })

  test('keeps the overall CTF trend above the matrix and moves the team trend into team detail', () => {
    expect(page).toContain('getLeaderboardTrendsEndpoint')
    expect(page).toContain("const visibleTrendSeries = computed<TrendSeries[]>")
    expect(page).toContain("teams.value")
    expect(page).toContain("$t('ui.overallScoreTrend')")
    expect(page).toContain('<component :is="LazyScoreTrendChart" v-else :series="visibleTrendSeries"')
    expect(page).toContain("<component :is=\"LazyScoreboardTeamDetailDialog\"")
    expect(page).toContain(':trend-series="teamDetailTrendSeries"')
    expect(page).not.toContain('selectedTrendTeamId')
    expect(page).not.toContain('selectedTrendSeries')
    expect(teamDetail).toContain("$t('ui.teamScoreTrend')")
    expect(teamDetail).toContain("<component :is=\"LazyScoreTrendChart\"")
    expect(teamDetail.indexOf('scoreboard-team-trend-title'))
      .toBeLessThan(teamDetail.indexOf('scoreboard-team-radar-title'))
  })

  test('keeps every team visible on hover and assigns series colors from a diverse theme palette', () => {
    expect(trendChart).toContain("colorBy: 'series' as const")
    expect(trendChart).toContain('hoverLink: false')
    expect(trendChart).toContain('selectedMode: false')
    expect(trendChart).toContain('emphasis: { disabled: true }')
    expect(trendChart).toContain('blur: { lineStyle: { opacity: 1 }, itemStyle: { opacity: 1 } }')
    expect(trendChart).not.toContain("focus: 'series'")
    expect(theme.match(/--chart-(?:[1-9]|10): oklch\([^)]+\);/g)).toHaveLength(20)
  })
})
