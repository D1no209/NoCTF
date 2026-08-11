import { describe, expect, test } from 'bun:test'

describe('participant challenge progress', () => {
  test('shows solve counts and a color-independent solved marker', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('current.solveCount += 1')
    expect(page).toContain('current.solvedByMyTeam = true')
    expect(page).toContain('challenge.currentScore')
    expect(page).toContain('本队结算')
    expect(page).toContain('bloodRankLabel')
    expect(page).toContain('支队伍已解出')
    expect(page).toContain('已解出')
    expect(page).toContain('<Flag')
    expect(page).toContain('leaderboardError.value = parseApiError')
    expect(page).toContain('leaderboardPending.value = true')
    expect(page).toContain('if (!leaderboard.value) return null')
    expect(page).toContain('v-if="!leaderboard && leaderboardError"')
  })

  test('hides the participant challenge tab before the competition starts', async () => {
    const shell = await Bun.file(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()

    expect(shell).toContain("competition.value?.status === 'Running'")
    expect(shell).toContain('...(challengesVisible ? [{ to: `${base}/challenges`, label: translate("题目"), icon: Puzzle }] : [])')
  })

  test('celebrates a correct flag once and respects reduced motion', async () => {
    const submit = await Bun.file(
      new URL('../app/components/challenges/FlagSubmit.vue', import.meta.url),
    ).text()

    expect(submit).toContain('celebrateCorrectFlag()')
    expect(submit).toContain('🎉')
    expect(submit).toContain('@media (prefers-reduced-motion: reduce)')
    expect(submit).toContain("if (wasPending && !isGameplayFactPending(data.state) && !toasted.has(id))")
  })
})
