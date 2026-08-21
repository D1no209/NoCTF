import { describe, expect, test } from 'bun:test'

describe('participant challenge progress', () => {
  test('shows solve counts and a color-independent solved marker', async () => {
    const page = await Bun.file(
      new URL('../app/pages/competitions/[id]/challenges/index.vue', import.meta.url),
    ).text()

    expect(page).toContain("scoreboardBreakdown(slot, 'Solve')")
    expect(page).toContain("scoreboardBreakdown(slot, 'Attack')")
    expect(page).toContain("scoreboardBreakdown(slot, 'Defense')")
    expect(page).toContain('progress.attackSucceeded && progress.defenseSucceeded')
    expect(page).toContain("if (progress.attackSucceeded) return translate('攻击成功')")
    expect(page).toContain("if (progress.defenseSucceeded) return translate('防御成功')")
    expect(page).toContain('本轮待结算')
    expect(page).not.toContain('currentBreakScore')
    expect(page).not.toContain('currentFixScore')
    expect(page).toContain('bloodRankLabel')
    expect(page).toContain('支队伍已解出')
    expect(page).toContain('已解出')
    expect(page).toContain('<Flag')
    expect(page).toContain('useScoreboardMatrix(competitionId)')
    expect(page).toContain('v-if="board.error.value"')
  })

  test('hides the challenge tab before start and from ineligible participants', async () => {
    const shell = await Bun.file(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()

    expect(shell).toContain("competition.value?.status === 'Running'")
    expect(shell).toContain("myTeam.value?.registrationStatus === 'Approved'")
    expect(shell).toContain('!myTeam.value.isBanned')
    expect(shell).toContain('hasCompetitionStaffAccess.value || hasParticipantChallengeAccess.value')
    expect(shell).toContain('...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate("题目"), icon: Puzzle }] : [])')
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
