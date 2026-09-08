import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('participant challenge progress', () => {
  test('shows solve counts and a color-independent solved marker', async () => {
    const navigator = await sourceFile(
      new URL('../app/features/competition/CompetitionChallengeNavigator.vue', import.meta.url),
    ).text()

    expect(navigator).toContain("scoreboardBreakdown(slot, 'Solve')")
    expect(navigator).toContain("scoreboardBreakdown(slot, 'Attack')")
    expect(navigator).toContain("scoreboardBreakdown(slot, 'Defense')")
    expect(navigator).toContain('progress.attackSucceeded && progress.defenseSucceeded')
    expect(navigator).toContain("if (progress.attackSucceeded) return translate(\"ui.attackSucceeded\")")
    expect(navigator).toContain("if (progress.defenseSucceeded) return translate(\"ui.defenseSucceeded\")")
    expect(navigator).toContain("ui.pendingRoundSettlement")
    expect(navigator).not.toContain('currentBreakScore')
    expect(navigator).not.toContain('currentFixScore')
    expect(navigator).toContain('bloodRankLabel')
    expect(navigator).toContain("ui.solvedByTeams")
    expect(navigator).toContain("ui.solved")
    expect(navigator).toContain('<Flag')
    expect(navigator).toContain('useScoreboardMatrix(props.competitionId)')
    expect(navigator).toContain('const hideSolved = ref(false)')
    expect(navigator).toContain('const collapsedDirections = ref<Set<string>>(new Set())')
    expect(navigator).toContain("group.challenges.filter(challenge => !progressFor(challenge.id)?.solvedByMyTeam)")
    expect(navigator).toContain('progress.attackSucceeded && progress.defenseSucceeded')
    expect(navigator).toContain(':aria-expanded="!isDirectionCollapsed(group.direction)"')
    expect(navigator).toContain('@click="toggleDirection(group.direction)"')
    expect(navigator).toContain('v-show="!isDirectionCollapsed(group.direction)"')
    expect(navigator).toContain('<Switch :id="`hide-solved-${competitionId}`" v-model="hideSolved" />')
    expect(navigator).toContain("$t('ui.noUnsolvedChallenges')")
    expect(navigator).toContain('scoreboardCurrentChallengeScore(board.snapshot.value, challengeId)')
    expect(navigator).toContain("{{ currentScore(challenge.id) }} {{ $t('ui.pts2') }}")
    expect(navigator).toContain('v-if="board.error.value"')
  })

  test('hides the challenge tab before start and from ineligible participants', async () => {
    const shell = await sourceFile(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()

    expect(shell).toContain("competition.value?.status === 'Running'")
    expect(shell).toContain("myTeam.value?.registrationStatus === 'Approved'")
    expect(shell).toContain('!myTeam.value.isBanned')
    expect(shell).toContain('hasCompetitionStaffAccess.value || hasParticipantChallengeAccess.value')
    expect(shell).toContain("...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate(\"ui.challenge\"), icon: Puzzle }] : [])")
  })

  test('celebrates a correct flag once and respects reduced motion', async () => {
    const submit = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()

    expect(submit).toContain('celebrateCorrectFlag()')
    expect(submit).toContain('celebrationParticles')
    expect(submit).toContain('<PartyPopper')
    expect(submit).toContain('flag-celebration-particle')
    expect(submit).toContain('resultTimer = setTimeout(closeResultDialog, 3200)')
    expect(submit).toContain('@pointer-down-outside="closeResultDialog"')
    expect(submit).toContain('@media (prefers-reduced-motion: reduce)')
    expect(submit).toContain("if (wasPending && !isGameplayFactPending(data.state) && !toasted.has(id))")
    expect(submit).toContain('resultDialog')
    expect(submit).toContain("$t('ui.submissionsRemaining'")
    expect(submit).toContain(':disabled="submitting || !input.trim() || attemptsExhausted"')
    expect(submit).not.toContain('v-for="item in tracked"')
  })

  test('uses the generated no-score practice judgement after a CTF competition finishes', async () => {
    const panel = await sourceFile(
      new URL('../app/features/challenges/panels/CtfPanel.vue', import.meta.url),
    ).text()
    const submit = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()
    const create = await sourceFile(
      new URL('../app/pages/admin/competitions/new.vue', import.meta.url),
    ).text()

    expect(panel).toContain('isCtfPracticeOpen(props.competition)')
    expect(panel).toContain(':practice="practiceOpen"')
    expect(submit).toContain('judgePracticeFlag({')
    expect(submit).toContain("ui.correctFlagPracticeAttemptsDoNotAwardPoints")
    expect(submit).not.toContain('judgePracticeFlagEndpoint')
    expect(create).toContain("practiceModeEnabled: mode.value === 'Ctf' && practiceModeEnabled.value")
  })

  test('embeds challenge-filtered submission history and refreshes it after acceptance', async () => {
    const detail = await sourceFile(
      new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url),
    ).text()
    const history = await sourceFile(
      new URL('../app/features/challenges/ChallengeSubmissionHistory.vue', import.meta.url),
    ).text()
    const submit = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()

    expect(detail).toContain("<component :is=\"ChallengeSubmissionHistory\"")
    expect(detail).toContain(':competition-challenge-id="competitionChallengeId"')
    expect(detail).toContain('@submitted="refreshSubmissionHistory"')
    expect(history).toContain('listGameplayFactsEndpoint({')
    expect(history).toContain('getGameplayFactValueEndpoint({')
    expect(history).toContain("submission.kind === 'FlagAttempt' || submission.kind === 'BreakAttempt'")
    expect(history).toContain('@click="openSubmittedValue(submission)"')
    expect(history).toContain("$t('ui.viewFlag')")
    expect(history).toContain('submittedValue.value = data.value ?? null')
    expect(history).toContain('valueError.value = parseApiError')
    expect(history).toContain('valueRequestGeneration += 1')
    expect(history).toContain('competitionChallengeId: props.competitionChallengeId')
    expect(history).toContain('createLatestPageRefresh')
    expect(history).toContain('getGameplayFactStatusEndpoint({')
    expect(history).toContain('if (!applyStatus(payload)) void refreshLatest()')
    expect(history).toContain('isGameplayFactPending(item.state)')
    expect(history).toContain('{ interval: 1500, timeout: 300_000 }')
    expect(history).toContain('stopPendingPolling()')
    expect(history).toContain('@click="loadNextPage"')
    expect(history).toContain('gameplayFactStateChanged: payload => {')
    expect(history).toContain('onReconnected: () => void refreshLatest()')
    expect(submit).toContain("emit('submitted', ids)")
    expect(submit).toContain("$t('ui.theEvaluationIsTakingLongerThanExpectedContinueTrackingIt')")
  })
})
