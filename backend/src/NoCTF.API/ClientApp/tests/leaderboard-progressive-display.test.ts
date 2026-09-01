import { describe, expect, test } from 'bun:test'

const page = await Bun.file(
  new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url),
).text()
const scoreboard = await Bun.file(
  new URL('../app/utils/scoreboard.ts', import.meta.url),
).text()
const slotStatus = await Bun.file(
  new URL('../app/components/leaderboard/ScoreboardSlotStatus.vue', import.meta.url),
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
    expect(page).toContain('<ScoreboardSlotStatus')
    expect(page).toContain("slot?.scoreState === 'Settled'")
    expect(page).toContain("slot?.scoreState === 'Settled' ? slot.netPoints ?? 0 : ''")
    expect(page).toContain('getScoreboardSlotDetailEndpoint')
    expect(page).toContain('getScoreboardAdjustmentDetailEndpoint')
    expect(page).toContain('(team.globalAdjustmentCount ?? 0) > 0')
    expect(page).toContain('完整调分记录均来自服务端权威事实。')
    expect(page).toContain('分值与状态均来自服务端权威结算结果。')
    expect(page).toContain("isCtf ? $t('分值来自服务端权威计分结果。') : $t('分值与状态均来自服务端权威结算结果。')")
    expect(page).toContain(":class=\"isCtf ? 'grid-cols-2' : 'grid-cols-3'\"")
    expect(page).toContain('<div v-if="!isCtf" class="rounded-lg border p-3">')
    expect(page).not.toContain('entry.attackScore')
    expect(page).not.toContain('entry.defenseScore')
  })

  test('renders server-authoritative CTF score trends above the matrix', () => {
    expect(page).toContain('getLeaderboardTrendsEndpoint')
    expect(page).toContain("const visibleTrendSeries = computed<TrendSeries[]>")
    expect(page).toContain("teams.value")
    expect(page).toContain("$t('总分趋势')")
    expect(page).toContain("$t('队伍得分趋势')")
    expect(page).toContain('<ScoreTrendChart v-else :series="visibleTrendSeries"')
    expect(page).toContain('<ScoreTrendChart v-else :series="selectedTrendSeries"')
    expect(page).toContain('v-if="isCtf" class="grid gap-4 xl:grid-cols-2"')
  })
})
