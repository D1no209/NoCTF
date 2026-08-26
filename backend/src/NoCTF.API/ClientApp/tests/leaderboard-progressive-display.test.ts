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

  test('keeps the participant team column compact while challenge columns use the remaining width', () => {
    expect(page).toContain('<Table class="table-fixed">')
    expect(page).toContain('w-44 min-w-44 max-w-44 border-r bg-card')
    expect(page).toContain(':title="displayTeamName(team)"')
    expect(page).toContain('min-w-0 flex-1 truncate')
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
    expect(page).not.toContain('entry.attackScore')
    expect(page).not.toContain('entry.defenseScore')
  })
})
