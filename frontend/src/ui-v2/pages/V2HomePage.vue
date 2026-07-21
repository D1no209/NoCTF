<script setup lang="ts">
import { computed } from 'vue'
import { useChromeSession } from '@/features/chrome/useChromeSession'
import type { CommandCompetition } from '../components/CommandCompetitionCard.vue'
import CommandHomeWorkspace, { type CommandHomeTeam } from '../components/CommandHomeWorkspace.vue'
import { useHomePage } from '@/features/home/useHomePage'

const { userName } = useChromeSession()

const {
  teams,
  activeCompetitions: watchedCompetitions,
  recentTeams: myRecentTeams,
  approvedTeams,
  pendingTeams,
  loadingCompetitions,
  loadingTeams,
  goCompetitions,
  goTeams,
  goCompetitionDetail,
  goCompetitionRegister,
} = useHomePage()

const operatorName = computed(() => userName.value || 'operator')

const competitionsToWatch = computed<CommandCompetition[]>(() => watchedCompetitions.value
  .filter((competition): competition is typeof competition & { id: string } => Boolean(competition.id))
  .map(competition => ({
    id: competition.id,
    title: competition.title?.trim() || 'Untitled competition',
    description: competition.description ?? null,
    status: competition.status?.trim() || 'Unknown',
    gameModeType: competition.gameModeType?.trim() || 'CTF',
    startTime: competition.startTime ?? '',
    endTime: competition.endTime ?? '',
  })))

const recentTeams = computed<CommandHomeTeam[]>(() => myRecentTeams.value
  .filter((team): team is typeof team & { id: string, competitionId: string } => Boolean(team.id && team.competitionId))
  .map(team => ({
    id: team.id,
    competitionId: team.competitionId,
    name: team.name?.trim() || 'Untitled team',
    competitionTitle: team.competitionTitle?.trim() || 'Untitled competition',
    registrationStatus: team.registrationStatus?.trim() || 'Unknown',
    gameModeType: team.gameModeType?.trim() || 'CTF',
    memberCount: team.memberCount ?? null,
    maxTeamMembers: team.maxTeamMembers ?? null,
  })))

const teamStats = computed(() => ({
  joined: teams.value?.length ?? 0,
  ready: approvedTeams.value,
  pending: pendingTeams.value,
}))
</script>

<template>
  <CommandHomeWorkspace
    :operator-name="operatorName"
    :competitions="competitionsToWatch"
    :loading-competitions="loadingCompetitions"
    :teams="recentTeams"
    :loading-teams="loadingTeams"
    :team-stats="teamStats"
    @browse-competitions="goCompetitions"
    @manage-teams="goTeams"
    @open-competition="goCompetitionDetail"
    @register-competition="goCompetitionRegister"
    @enter-competition="goCompetitionDetail"
  />
</template>
