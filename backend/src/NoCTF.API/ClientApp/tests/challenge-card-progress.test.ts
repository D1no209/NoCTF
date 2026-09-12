import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { challengeProgressIcon } from '../app/features/competition/challenge-progress-icon'

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
    expect(navigator).toContain("ui.attackAndDefenseSucceeded")
    expect(challengeProgressIcon({ solvedByMyTeam: true, attackSucceeded: false, defenseSucceeded: false }, false)).toBe('solved')
    expect(challengeProgressIcon({ solvedByMyTeam: false, attackSucceeded: true, defenseSucceeded: false }, true)).toBe('attack-success')
    expect(challengeProgressIcon({ solvedByMyTeam: false, attackSucceeded: false, defenseSucceeded: true }, true)).toBe('defense-success')
    expect(challengeProgressIcon({ solvedByMyTeam: true, attackSucceeded: true, defenseSucceeded: true }, true)).toBe('attack-defense-success')
    expect(challengeProgressIcon({ solvedByMyTeam: false, attackSucceeded: false, defenseSucceeded: false }, true)).toBeNull()
    expect(navigator).toContain('<StatusIcon')
    expect(navigator).not.toContain('<Flag')
    expect(navigator).toContain('useScoreboardMatrix(props.competitionId)')
    expect(navigator).toContain('const hideSolved = ref(false)')
    expect(navigator).toContain("const search = ref('')")
    expect(navigator).toContain("(challenge.title ?? '').toLocaleLowerCase().includes(normalizedSearch.value)")
    expect(navigator).toContain('challengeNavigator.searchPlaceholder')
    expect(navigator).toContain('<div class="flex min-w-0 items-center gap-3">')
    expect(navigator).toContain('<h2 class="shrink-0 text-base font-semibold">')
    expect(navigator).toContain('class="min-w-0 flex-1"')
    expect(navigator).toContain('const groupOptions = computed(')
    expect(navigator).toContain('!hideSolved.value || !progressFor(challenge.id)?.solvedByMyTeam')
    expect(navigator).toContain('progress.attackSucceeded && progress.defenseSucceeded')
    expect(navigator).toContain(':groups="groupOptions"')
    expect(navigator).toContain('@update:model-value="selectChallenge"')
    expect(navigator).toContain('groupOptions.value.flatMap(group => group.items)')
    expect(navigator).toContain('<Switch :id="`hide-solved-${competitionId}`" v-model="hideSolved" />')
    expect(navigator).toContain("translate('ui.noUnsolvedChallenges')")
    expect(navigator).toContain('scoreboardCurrentChallengeScore(board.snapshot.value, challengeId)')
    expect(navigator).toContain('{{ currentScore(item.challenge.id) }}')
    expect(navigator).toContain("{{ $t('ui.pts2') }}")
    expect(navigator).toContain('v-else-if="board.error.value"')
    expect(navigator).toContain('slot.entries?.find(entry => entry.award)?.award')
    expect(navigator).toContain('teamName: team.teamName?.trim() || null')
    expect(navigator).toContain('<BloodMark :rank="blood.rank"')
    expect(navigator).toContain(':content="bloodTooltip(blood)"')
    expect(navigator).toContain('return `${bloodRankLabel(blood.rank)} · ${bloodTeamName(blood)}`')
    expect(navigator).not.toContain('{{ bloodTeamName(blood) }}')
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

  test('celebrates a correct flag and keeps CTF judging available after a solve', async () => {
    const submit = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()
    const panel = await sourceFile(
      new URL('../app/features/challenges/panels/CtfPanel.vue', import.meta.url),
    ).text()

    expect(submit).toContain('celebrateCorrectFlag()')
    expect(submit).toContain('celebrationParticles')
    expect(submit).toContain('<PartyPopper')
    expect(submit).toContain('flag-celebration-particle')
    expect(submit).not.toContain('resultTimer')
    expect(submit).not.toContain('<Dialog :open="resultDialog')
    expect(submit).toContain('@media (prefers-reduced-motion: reduce)')
    expect(submit).toContain("if (wasPending && !isGameplayFactPending(data.state) && !toasted.has(id))")
    expect(submit).not.toContain('resultDialog')
    expect(submit).not.toContain("$t('ui.submissionsRemaining'")
    expect(submit).toContain("emit('remainingChanged', remainingAttempts.value)")
    expect(submit).toContain(':pending="submitting" :disabled="inputDisabled"')
    expect(submit).toContain(":placeholder=\"solved ? $t('terminal.challengeSolved') : $t('terminal.flagPlaceholder')\"")
    expect(submit).not.toContain("showResult(true, translate('terminal.challengeSolved'))")
    expect(submit).toContain('solved.value = true')
    expect(submit).toContain('solvedChallengeKeys.add(challengeKey())')
    expect(submit).toContain('props.initiallySolved || solvedChallengeKeys.has(challengeKey())')
    expect(submit).toContain('watch(() => props.initiallySolved')
    expect(panel).toContain(':initially-solved="challenge.solvedByMyTeam"')
    expect(submit).toContain('const inputDisabled = computed(() => attemptsExhausted.value)')
    expect(submit).not.toContain('attemptsExhausted.value || solved.value')
    expect(submit).not.toContain('v-for="item in tracked"')
  })

  test('uses the standard fact submission flow after a CTF competition finishes', async () => {
    const panel = await sourceFile(
      new URL('../app/features/challenges/panels/CtfPanel.vue', import.meta.url),
    ).text()
    const submit = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()
    const create = await sourceFile(
      new URL('../app/features/competitions/CreateCompetitionDialog.vue', import.meta.url),
    ).text()

    expect(panel).toContain('isCtfPracticeOpen(props.competition)')
    expect(panel).toContain(':practice="practiceOpen"')
    expect(submit).toContain('submitFlagEndpoint({')
    expect(submit).toContain("ui.correctFlagPracticeAttemptsDoNotAwardPoints")
    expect(submit).not.toContain('judgePracticeFlag')
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
