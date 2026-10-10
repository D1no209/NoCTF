import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { challengeProgressIcon } from '../app/features/competition/challenge-progress-icon'
import { affectsCompetitionChallengeList, isChallengeVisible } from '../app/features/competition/useCompetitionChallengeNavigator'

describe('participant challenge progress', () => {
  test('shows solve counts and a color-independent solved marker', async () => {
    const navigator = await sourceFile(
      new URL('../app/features/competition/CompetitionChallengeNavigator.vue', import.meta.url),
    ).text()

    expect(navigator).toContain("scoreboardBreakdown(slot, 'Solve')")
    expect(navigator).toContain("scoreboardBreakdown(slot, 'Attack')")
    expect(navigator).toContain("scoreboardBreakdown(slot, 'Defense')")
    expect(navigator).toContain('progress.attackSucceeded && progress.defenseSucceeded')
    expect(navigator).toContain("if (progress.attackSucceeded) return translate(\"common.label.attackSucceeded\")")
    expect(navigator).toContain("if (progress.defenseSucceeded) return translate(\"common.label.defenseSucceeded\")")
    expect(navigator).toContain("common.label.pendingRoundSettlement")
    expect(navigator).not.toContain('currentBreakScore')
    expect(navigator).not.toContain('currentFixScore')
    expect(navigator).toContain('bloodRankLabel')
    expect(navigator).toContain("challenges.label.solvedTeams")
    expect(navigator).toContain("common.label.solved.competitionChallengeNavigator")
    expect(navigator).toContain("common.label.attackDefenseSucceeded")
    expect(challengeProgressIcon({ solvedByMyTeam: true, attackSucceeded: false, defenseSucceeded: false }, false)).toBe('solved')
    expect(challengeProgressIcon({ solvedByMyTeam: false, attackSucceeded: true, defenseSucceeded: false }, true)).toBe('attack-success')
    expect(challengeProgressIcon({ solvedByMyTeam: false, attackSucceeded: false, defenseSucceeded: true }, true)).toBe('defense-success')
    expect(challengeProgressIcon({ solvedByMyTeam: true, attackSucceeded: true, defenseSucceeded: true }, true)).toBe('attack-defense-success')
    expect(challengeProgressIcon({ solvedByMyTeam: false, attackSucceeded: false, defenseSucceeded: false }, true)).toBeNull()
    expect(navigator).toContain('<StatusIcon')
    expect(navigator).not.toContain('<Flag')
    expect(navigator).toContain('useScoreboardMatrix(props.competitionId)')
    expect(navigator).toContain('const hideSolved = ref(false)')
    expect(navigator).toContain('const hideLocked = ref(false)')
    expect(navigator).toContain("const search = ref('')")
    expect(navigator).toContain("(challenge.title ?? '').toLocaleLowerCase().includes(filters.search)")
    expect(navigator).toContain('challengeNavigator.searchPlaceholder')
    expect(navigator).toContain('<div class="flex min-w-0 items-center gap-3">')
    expect(navigator).toContain('<h2 class="shrink-0 text-base font-semibold">')
    expect(navigator).toContain('class="min-w-0 flex-1"')
    expect(navigator).toContain('const groupOptions = computed(')
    expect(navigator).toContain('isChallengeVisible(challenge, {')
    expect(navigator).toContain('hideLocked: hidesLockedChallenges.value')
    expect(navigator).toContain('progress.attackSucceeded && progress.defenseSucceeded')
    expect(navigator).toContain(':groups="groupOptions"')
    expect(navigator).toContain('@update:model-value="selectChallenge"')
    expect(navigator).toContain('groupOptions.value.flatMap(group => group.items)')
    expect(navigator).toContain('<Switch :id="`hide-solved-${competitionId}`" v-model="hideSolved" />')
    expect(navigator).toContain('v-if="isCtf"')
    expect(navigator).toContain('<Switch :id="`hide-locked-${competitionId}`" v-model="hideLocked" />')
    expect(navigator).toContain("translate('challenges.label.unsolvedChallenges')")
    expect(navigator).toContain('scoreboardCurrentChallengeScore(board.snapshot.value, challengeId)')
    expect(navigator).toContain('{{ currentScore(item.challenge.id) }}')
    expect(navigator).toContain("{{ $t('common.label.pts.scoreTrendChart') }}")
    expect(navigator).toContain('v-else-if="board.error.value"')
    expect(navigator).toContain('slot.entries?.find(entry => entry.award)?.award')
    expect(navigator).toContain('teamName: team.teamName?.trim() || null')
    expect(navigator).toContain('earnedByMyTeam: team.teamId === myTeamId.value')
    expect(navigator).toContain('<BloodMark :rank="blood.rank"')
    expect(navigator).toContain(':highlighted="blood.earnedByMyTeam"')
    expect(navigator).toContain(':content="bloodTooltip(blood)"')
    expect(navigator).toContain('return `${bloodRankLabel(blood.rank)} · ${bloodTeamName(blood)}`')
    expect(navigator).not.toContain('{{ bloodTeamName(blood) }}')
    expect(navigator).toContain('competitionEventChanged: notification => {')
    expect(navigator).toContain('onReconnected: () => void refreshChallenges()')
    expect(navigator).toContain('const refreshChallenges = createTrailingRefresh(loadChallenges)')
  })

  test('can hide locked challenges independently or together with solved and search filters', () => {
    const challenge = { title: 'Orbiting Headers', locked: true }
    const filters = { hideSolved: false, hideLocked: false, solvedByMyTeam: false, search: '' }
    expect(isChallengeVisible(challenge, filters)).toBeTrue()
    expect(isChallengeVisible(challenge, { ...filters, hideLocked: true })).toBeFalse()
    expect(isChallengeVisible({ ...challenge, locked: false }, { ...filters, hideLocked: true })).toBeTrue()
    expect(isChallengeVisible(challenge, { ...filters, hideSolved: true, solvedByMyTeam: true })).toBeFalse()
    expect(isChallengeVisible({ ...challenge, locked: false }, {
      ...filters, hideSolved: true, hideLocked: true, solvedByMyTeam: true,
    })).toBeFalse()
    expect(isChallengeVisible({ ...challenge, locked: false }, { ...filters, search: 'headers' })).toBeTrue()
    expect(isChallengeVisible({ ...challenge, locked: false }, { ...filters, search: 'crypto' })).toBeFalse()
  })

  test('refreshes the challenge list for every event that can change participant visibility', () => {
    expect(affectsCompetitionChallengeList('ChallengeCreated')).toBeTrue()
    expect(affectsCompetitionChallengeList('ChallengeUpdated')).toBeTrue()
    expect(affectsCompetitionChallengeList('ChallengePublished')).toBeTrue()
    expect(affectsCompetitionChallengeList('ChallengeUnpublished')).toBeTrue()
    expect(affectsCompetitionChallengeList('ChallengeDeleted')).toBeTrue()
    expect(affectsCompetitionChallengeList('ChallengeDescriptionUpdated')).toBeTrue()
    expect(affectsCompetitionChallengeList('HintPublished')).toBeFalse()
  })

  test('hides the challenge tab before start and from ineligible participants', async () => {
    const shell = await sourceFile(
      new URL('../app/pages/competitions/[id].vue', import.meta.url),
    ).text()

    expect(shell).toContain("competition.value?.status === 'Running'")
    expect(shell).toContain("myTeam.value?.registrationStatus === 'Approved'")
    expect(shell).toContain('!myTeam.value.isBanned')
    expect(shell).toContain('hasCompetitionStaffAccess.value || hasParticipantChallengeAccess.value')
    expect(shell).toContain("...(challengesVisible && canReadChallenges ? [{ to: `${base}/challenges`, label: translate(\"common.label.challenge.pageTitle\"), icon: Puzzle }] : [])")
  })

  test('celebrates a correct flag prominently and locks judging after a solve', async () => {
    const submit = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()
    const celebrationMotion = await Bun.file(
      new URL('../app/motion/flag-celebration.css', import.meta.url),
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
    expect(celebrationMotion).toContain('@media (prefers-reduced-motion: reduce)')
    expect(celebrationMotion).toContain('.flag-celebration-layer')
    expect(submit).toContain("if (wasPending && !isGameplayFactPending(data.state) && !toasted.has(id))")
    expect(submit).not.toContain('resultDialog')
    expect(submit).not.toContain("$t('common.label.submissionsRemaining'")
    expect(submit).toContain("emit('remainingChanged', remainingAttempts.value)")
    expect(submit).toContain(':pending="submitting" :disabled="inputDisabled"')
    expect(submit).toContain(":placeholder=\"solved ? $t('terminal.challengeSolved') : $t('terminal.flagPlaceholder')\"")
    expect(submit).toContain("showResult(true, translate('terminal.challengeSolved'))")
    expect(submit).toContain('solved.value = true')
    expect(submit).toContain('solvedChallengeKeys.add(challengeKey())')
    expect(submit).toContain('props.initiallySolved || solvedChallengeKeys.has(challengeKey())')
    expect(submit).toContain('watch(() => props.initiallySolved')
    expect(panel).toContain(':initially-solved="challenge.solvedByMyTeam"')
    expect(submit).toContain('submissionsClosed.value')
    expect(submit).toContain('!judgementOnly.value')
    expect(submit).toContain('<Alert v-if="persistentResult?.correct === true"')
    expect(submit).toContain('if (persistentResult.value?.correct !== true) persistentResult.value = null')
    expect(submit).not.toContain('v-for="item in tracked"')
  })

  test('announces a solve only for the current successful submission, not restored solved state', async () => {
    const submit = await sourceFile(
      new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url),
    ).text()

    expect(submit).toContain("const persistentResult = ref<{ correct: boolean | null; message: UiMessage } | null>(null)")
    expect(submit).toContain('const solved = ref(props.initiallySolved || solvedChallengeKeys.has(challengeKey()))')
    expect(submit).toContain("showResult(true, translate('terminal.challengeSolved'))")
    expect(submit).toContain('<Alert v-if="persistentResult?.correct === true"')
    expect(submit).not.toContain('<Alert v-if="solved"')
    expect(submit).toContain('persistentResult.value = null')
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
    expect(submit).toContain("challenges.flagSubmit.description.correctFlagPracticeAttempts")
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
    expect(history).toContain("$t('challenges.label.viewFlag')")
    expect(history).toContain('submittedValue.value = data.value ?? null')
    expect(history).toContain('valueError.value = parseApiError')
    expect(history).toContain('valueRequestGeneration += 1')
    expect(history).toContain('competitionChallengeId: props.competitionChallengeId')
    expect(history).toContain('createLatestPageRefresh')
    expect(history).toContain('await readStatus(props.competitionId, submission.id!)')
    expect(history).toContain('if (!applyStatus(payload)) void refreshLatest()')
    expect(history).toContain('isGameplayFactPending(item.state)')
    expect(history).toContain('{ interval: 1500, timeout: 300_000 }')
    expect(history).toContain('stopPendingPolling()')
    expect(history).toContain('@click="loadNextPage"')
    expect(history).toContain('gameplayFactStateChanged: payload => {')
    expect(history).toContain('onReconnected: () => void refreshLatest()')
    expect(submit).toContain("emit('submitted', ids)")
    expect(submit).toContain("$t('challenges.flagSubmit.description.evaluationTakingLongerExpected')")
  })
})
