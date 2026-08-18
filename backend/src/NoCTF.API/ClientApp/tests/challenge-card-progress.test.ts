import { describe, expect, test } from 'bun:test'

describe('participant challenge progress', () => {
  test('shows solve counts and a color-independent solved marker', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain('current.solveCount += 1')
    expect(page).toContain('current.attackSucceeded && current.defenseSucceeded')
    expect(page).toContain("if (progress.attackSucceeded) return translate('攻击成功')")
    expect(page).toContain("if (progress.defenseSucceeded) return translate('防御成功')")
    expect(page).toContain('currentScoreFor(challenge)')
    expect(page).toContain('currentBreakScore')
    expect(page).toContain('currentFixScore')
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

  test('uses the generated no-score practice judgement after a CTF competition finishes', async () => {
    const panel = await Bun.file(
      new URL('../app/components/challenges/panels/CtfPanel.vue', import.meta.url),
    ).text()
    const submit = await Bun.file(
      new URL('../app/components/challenges/FlagSubmit.vue', import.meta.url),
    ).text()
    const create = await Bun.file(
      new URL('../app/pages/admin/competitions/new.vue', import.meta.url),
    ).text()

    expect(panel).toContain("competition.status === 'Finished' && competition.practiceModeEnabled === true")
    expect(submit).toContain('judgePracticeFlag({')
    expect(submit).toContain('Flag 正确；本次练习不计分')
    expect(submit).not.toContain('judgePracticeFlagEndpoint')
    expect(create).toContain("practiceModeEnabled: mode.value === 'Ctf' && practiceModeEnabled.value")
  })
})
