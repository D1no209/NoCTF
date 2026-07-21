<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandChallengeConsole from '../components/CommandChallengeConsole.vue'
import CommandCompetitionDetailWorkspace, {
  type CommandCompetitionDetail,
} from '../components/CommandCompetitionDetailWorkspace.vue'
import type { CommandChallenge } from '../components/CommandChallengeGrid.vue'
import type { CommandLeaderboardEntry } from '../components/CommandLeaderboardTable.vue'
import { useCompetitionDetailPage } from '@/features/competitions/useCompetitionDetailPage'
import { useCompetitionLeaderboard } from '@/features/competitions/useCompetitionLeaderboard'

const {
  competitionId,
  hasValidCompetitionId,
  competition: competitionRaw,
  loadingCompetition,
  competitionError,
  competitionQueryError,
  refetchCompetition,
  loadingChallenges,
  challengesError,
  loadingMyTeams,
  isAwdpMode,
  approvedTeam,
  effectiveChallenges,
  selectedChallenge,
  selectChallenge,
  selectedChallengeAwdpState,
  canSubmitSelectedChallenge,
  awdpStateLoading,
  awdpStateError,
  onChallengeSolved,
  goCompetitions,
  goRegistration,
  goAwdpScreen,
  goAwdDashboard,
  goKohDashboard,
  goPenetrationDashboard,
} = useCompetitionDetailPage()

const {
  leaderboard: leaderboardRaw,
  loadingLeaderboard,
  leaderboardError,
} = useCompetitionLeaderboard()

const challengeConsoleOpen = ref(false)

const competition = computed<CommandCompetitionDetail | undefined>(() => {
  const source = competitionRaw.value
  if (!source?.id)
    return undefined

  return {
    id: source.id,
    title: source.title?.trim() || 'Untitled competition',
    description: source.description ?? null,
    status: source.status?.trim() || 'Unknown',
    gameModeType: source.gameModeType?.trim() || 'CTF',
    startTime: source.startTime ?? null,
    endTime: source.endTime ?? null,
    maxTeamMembers: source.maxTeamMembers ?? null,
    tracksEnabled: source.tracksEnabled ?? false,
    trackNames: source.trackNames ?? [],
  }
})

const challenges = computed<CommandChallenge[]>(() => effectiveChallenges.value
  .filter((challenge): challenge is typeof challenge & { id: string } => Boolean(challenge.id))
  .map(challenge => ({
    id: challenge.id,
    title: challenge.title?.trim() || 'Untitled challenge',
    description: challenge.description ?? null,
    typeId: challenge.typeId?.trim() || 'misc',
    direction: challenge.direction?.trim() || 'misc',
    points: challenge.points ?? 0,
    solveCount: challenge.solveCount ?? 0,
  })))

const leaderboard = computed<CommandLeaderboardEntry[]>(() => leaderboardRaw.value?.entries ?? [])

const submissionUnavailableReason = computed(() => {
  if (loadingMyTeams.value)
    return 'Checking team enrollment before enabling flag submission.'
  if (!approvedTeam.value)
    return 'Register with an approved team before submitting flags.'
  if (approvedTeam.value.isBanned)
    return 'This team is restricted from participant actions.'
  if (!isAwdpMode.value)
    return ''
  if (awdpStateLoading.value)
    return 'Synchronizing AWDP task state before enabling flag submission.'
  if (awdpStateError.value)
    return 'AWDP task state could not be verified, so flag submission is unavailable.'
  if (!selectedChallengeAwdpState.value)
    return 'No active AWDP state was returned for this challenge.'
  if (selectedChallengeAwdpState.value.canSubmitFlag === false)
    return 'Flag submission is not available for this challenge in the current round.'
  return ''
})

const workspaceState = computed<'invalid' | 'loading' | 'error' | 'ready'>(() => {
  if (!hasValidCompetitionId.value)
    return 'invalid'
  if (loadingCompetition.value)
    return 'loading'
  if (competitionError.value || !competition.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const error = competitionQueryError.value
  return error instanceof Error ? error.message : undefined
})

function openChallengeConsole(challenge: CommandChallenge) {
  selectChallenge(challenge)
  challengeConsoleOpen.value = true
}
</script>

<template>
  <CommandCompetitionDetailWorkspace
    :state="workspaceState"
    :error-message="errorMessage"
    :competition="competition"
    :challenges="challenges"
    :leaderboard="leaderboard"
    :loading-challenges="loadingChallenges"
    :loading-leaderboard="loadingLeaderboard"
    :challenge-error="challengesError"
    :leaderboard-error="leaderboardError"
    @back="goCompetitions"
    @register="goRegistration"
    @retry="refetchCompetition"
    @open-awdp-screen="goAwdpScreen"
    @open-awd-dashboard="goAwdDashboard"
    @open-koh-dashboard="goKohDashboard"
    @open-penetration-dashboard="goPenetrationDashboard"
    @select-challenge="openChallengeConsole"
  />

  <CommandChallengeConsole
    v-model:open="challengeConsoleOpen"
    :competition-id="competitionId"
    :challenge="selectedChallenge"
    :can-submit="canSubmitSelectedChallenge"
    :submission-unavailable-reason="submissionUnavailableReason"
    @submitted="onChallengeSolved"
  />
</template>
