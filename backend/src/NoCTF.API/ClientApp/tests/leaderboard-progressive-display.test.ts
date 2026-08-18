import { describe, expect, test } from 'bun:test'

const page = await Bun.file(
  new URL('../app/pages/competitions/[id]/leaderboard.vue', import.meta.url),
).text()

describe('leaderboard progressive display', () => {
  test('keeps the full snapshot and progressively reveals stable entries', () => {
    expect(page).toContain('const entryBatchSize = 50')
    expect(page).toContain('entries.value.slice(0, visibleEntryCount.value)')
    expect(page).toContain('v-for="entry in visibleEntries"')
    expect(page).toContain('v-if="hasMoreEntries"')
    expect(page).toContain('@click="showMoreEntries"')
  })

  test('does not recreate numbered client-side pagination', () => {
    expect(page).not.toContain('const pageSize =')
    expect(page).not.toContain('const totalPages =')
    expect(page).not.toContain('@click="page--"')
    expect(page).not.toContain('@click="page++"')
  })

  test('renders AWDP as settled attack and defense scores', async () => {
    const detail = await Bun.file(
      new URL('../app/components/leaderboard/TeamDetailDialog.vue', import.meta.url),
    ).text()

    expect(page).toContain('leaderboard.currentRound')
    expect(page).toContain('leaderboard.settledThroughRound')
    expect(page).toContain('本轮攻击与防御成绩将在轮次结束后统一结算')
    expect(page).toContain('entry.attackScore')
    expect(page).toContain('entry.defenseScore')
    expect(page).toContain('entry.penaltyScore')
    expect(detail).toContain('题目攻击与防御得分')
    expect(detail).toContain('entry.attackScore')
    expect(detail).toContain('entry.defenseScore')
    expect(detail).toContain('entry.penaltyScore')
  })
})
