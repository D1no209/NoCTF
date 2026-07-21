<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandAdminCompetitionDetailWorkspace, {
  type CommandAdminCompetitionChallenge,
  type CommandAdminCompetitionCheatIncident,
  type CommandAdminCompetitionLog,
  type CommandAdminCompetitionTeam,
} from '../components/CommandAdminCompetitionDetailWorkspace.vue'
import { useAdminCompetitionDetailPage } from '@/features/admin/useAdminCompetitionDetailPage'

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const {
  competitionForm,
  competition,
  loadingCompetition,
  competitionChallenges,
  loadingChallenges,
  competitionTeams,
  loadingTeams,
  competitionLogs,
  loadingLogs,
  cheatIncidents,
  loadingCheatIncidents,
  saveCompetitionMutation,
  deleteChallengeMutation,
  approveTeamMutation,
  rejectTeamMutation,
  lockTeamMutation,
  banTeamMutation,
  unbanTeamMutation,
  restartContainerMutation,
  rebuildScoreboardMutation,
  activeSection,
  switchSection,
  canOpenAwdpScreen,
  goAdminCompetitions,
  goOperations,
  goAwdpScreen,
  goCreateChallenge,
  goEditChallenge,
} = useAdminCompetitionDetailPage()

const challenges = computed<CommandAdminCompetitionChallenge[]>(() => (competitionChallenges.value ?? [])
  .filter(challenge => Boolean(challenge.id)))

const teams = computed<CommandAdminCompetitionTeam[]>(() => (competitionTeams.value ?? [])
  .filter(team => Boolean(team.id)))

const logs = computed<CommandAdminCompetitionLog[]>(() => (competitionLogs.value ?? [])
  .filter(log => Boolean(log.id)))

const cheats = computed<CommandAdminCompetitionCheatIncident[]>(() => (cheatIncidents.value ?? [])
  .filter(incident => Boolean(incident.id)))

const teamActionPending = computed(() =>
  approveTeamMutation.isPending.value
  || rejectTeamMutation.isPending.value
  || lockTeamMutation.isPending.value
  || banTeamMutation.isPending.value
  || unbanTeamMutation.isPending.value)

function reportError(error: unknown, fallback: string) {
  operationTone.value = 'danger'
  operationMessage.value = error instanceof Error ? error.message : fallback
}

function reportSuccess(message: string) {
  operationTone.value = 'success'
  operationMessage.value = message
}

function save() {
  saveCompetitionMutation.mutate(undefined, {
    onSuccess: () => reportSuccess('Competition settings saved.'),
    onError: error => reportError(error, 'Unable to save the competition settings.'),
  })
}

function deleteChallenge(challengeId: string) {
  deleteChallengeMutation.mutate(challengeId, {
    onSuccess: () => reportSuccess('Challenge removed from the competition.'),
    onError: error => reportError(error, 'Unable to remove the challenge.'),
  })
}

function restartContainer(challengeId: string) {
  restartContainerMutation.mutate(challengeId, {
    onSuccess: () => reportSuccess('Container restart requested.'),
    onError: error => reportError(error, 'Unable to restart the container.'),
  })
}

function approve(teamId: string) {
  approveTeamMutation.mutate(teamId, {
    onSuccess: () => reportSuccess('Team approved.'),
    onError: error => reportError(error, 'Unable to update the team.'),
  })
}

function reject(teamId: string) {
  rejectTeamMutation.mutate(teamId, {
    onSuccess: () => reportSuccess('Team rejected.'),
    onError: error => reportError(error, 'Unable to update the team.'),
  })
}

function toggleLock(payload: { teamId: string, isLocked: boolean }) {
  lockTeamMutation.mutate(payload, {
    onSuccess: () => reportSuccess('Team lock updated.'),
    onError: error => reportError(error, 'Unable to update the team.'),
  })
}

function banTeam(teamId: string) {
  banTeamMutation.mutate(teamId, {
    onSuccess: () => reportSuccess('Team banned.'),
    onError: error => reportError(error, 'Unable to update the team.'),
  })
}

function unbanTeam(teamId: string) {
  unbanTeamMutation.mutate(teamId, {
    onSuccess: () => reportSuccess('Team unbanned.'),
    onError: error => reportError(error, 'Unable to update the team.'),
  })
}

function toggleBan(payload: { teamId: string, isBanned: boolean }) {
  if (payload.isBanned)
    unbanTeam(payload.teamId)
  else
    banTeam(payload.teamId)
}

function rebuild() {
  rebuildScoreboardMutation.mutate(undefined, {
    onSuccess: result => reportSuccess(`Scoreboard rebuilt${result.rows === undefined ? '' : ` (${result.rows} teams)`}.`),
    onError: error => reportError(error, 'Unable to rebuild the scoreboard.'),
  })
}
</script>

<template>
  <CommandAdminCompetitionDetailWorkspace
    v-model:competition-form="competitionForm"
    :loading="loadingCompetition"
    :competition-title="competition?.title ?? ''"
    :competition-status="competition?.status ?? ''"
    :can-open-awdp-screen="canOpenAwdpScreen"
    :active-section="activeSection"
    :challenges="challenges"
    :loading-challenges="loadingChallenges"
    :teams="teams"
    :loading-teams="loadingTeams"
    :logs="logs"
    :loading-logs="loadingLogs"
    :cheats="cheats"
    :loading-cheats="loadingCheatIncidents"
    :saving="saveCompetitionMutation.isPending.value"
    :team-action-pending="teamActionPending"
    :restart-pending="restartContainerMutation.isPending.value"
    :rebuild-pending="rebuildScoreboardMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @switch-section="switchSection"
    @save="save"
    @create-challenge="goCreateChallenge"
    @edit-challenge="goEditChallenge"
    @delete-challenge="deleteChallenge"
    @restart-container="restartContainer"
    @approve="approve"
    @reject="reject"
    @toggle-lock="toggleLock"
    @toggle-ban="toggleBan"
    @ban-team="banTeam"
    @rebuild="rebuild"
    @back="goAdminCompetitions"
    @open-operations="goOperations"
    @open-awdp-screen="goAwdpScreen"
  />
</template>
