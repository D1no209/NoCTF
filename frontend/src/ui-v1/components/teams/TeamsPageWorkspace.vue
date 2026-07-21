<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import AppLayout from '@/ui-v1/components/layout/AppLayout.vue'
import PageHeader from '@/ui-v1/components/layout/PageHeader.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Button } from '@/ui-v1/components/ui/button'
import { Users } from 'lucide-vue-next'
import TeamsWorkspace from '@/ui-v1/components/teams/TeamsWorkspace.vue'
import { useTeamsPage, type MyTeamDto } from '@/features/teams/useTeamsPage'

const { t } = useI18n()

const {
  joinToken,
  newTeamName,
  selectedCompetitionId,
  selectedTrackName,
  teams: myTeams,
  isLoading,
  isError,
  refetch,
  activeTeams: rawActiveTeams,
  bannedTeams: rawBannedTeams,
  availableCompetitions: rawCompetitions,
  loadingCompetitions,
  selectedCompetitionRequiresTrack,
  selectedCompetitionTracks,
  loadingSelectedCompetition,
  canCreateTeam,
  createTeamMutation: createTeam,
  joinByTokenMutation: joinByToken,
  leaveTeamMutation: leaveTeam,
  copyToken: copyTokenToClipboard,
} = useTeamsPage()

function mapTeam(team: MyTeamDto) {
  return {
    id: team.id ?? '',
    competitionId: team.competitionId ?? '',
    name: team.name ?? '',
    inviteToken: team.inviteToken ?? '',
    registrationStatus: team.registrationStatus ?? '',
    competitionTitle: team.competitionTitle ?? '',
    competitionStatus: team.competitionStatus ?? '',
    gameModeType: team.gameModeType ?? '',
    memberCount: team.memberCount ?? 0,
    maxTeamMembers: team.maxTeamMembers ?? 0,
    isCaptain: team.isCaptain ?? false,
    isLocked: team.isLocked ?? false,
    isBanned: team.isBanned ?? false,
    trackName: team.trackName,
  }
}

const teams = computed(() => (myTeams.value ?? []).map(mapTeam))
const activeTeams = computed(() => rawActiveTeams.value.map(mapTeam))
const bannedTeams = computed(() => rawBannedTeams.value.map(mapTeam))
const availableCompetitions = computed(() => rawCompetitions.value.map(competition => ({
  id: competition.id ?? '',
  title: competition.title ?? '',
  status: competition.status ?? '',
  gameModeType: competition.gameModeType ?? '',
})))

const createTeamMutation = useToastMutation(createTeam, {
  success: 'teams.createSuccess',
  error: 'teams.actionError',
})

const joinByTokenMutation = useToastMutation(joinByToken, {
  success: 'teams.joinSuccess',
  error: 'teams.actionError',
})

const leaveTeamMutation = useToastMutation<string>(leaveTeam, {
  success: 'teams.leaveSuccess',
  error: 'teams.leaveError',
})

async function copyToken(token: string) {
  if (await copyTokenToClipboard(token))
    toast.success(t('teams.tokenCopied'))
}
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1600px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
      <div class="flex flex-col gap-4 md:flex-row md:items-end md:justify-between">
        <PageHeader :title="t('teams.pageTitle')" :description="t('teams.pageDescription')" />
        <Button as-child>
          <RouterLink to="/competitions">
            <Users class="size-4" />
            {{ t('teams.findCompetition') }}
          </RouterLink>
        </Button>
      </div>

      <TeamsWorkspace
        v-model:selected-competition-id="selectedCompetitionId"
        v-model:selected-track-name="selectedTrackName"
        v-model:new-team-name="newTeamName"
        v-model:join-token="joinToken"
        :teams="teams"
        :active-teams="activeTeams"
        :banned-teams="bannedTeams"
        :is-loading="isLoading"
        :is-error="isError"
        :loading-competitions="loadingCompetitions"
        :available-competitions="availableCompetitions"
        :selected-competition-tracks="selectedCompetitionTracks"
        :selected-competition-requires-track="selectedCompetitionRequiresTrack"
        :loading-selected-competition="loadingSelectedCompetition"
        :can-create-team="canCreateTeam"
        :create-pending="createTeamMutation.isPending.value"
        :join-pending="joinByTokenMutation.isPending.value"
        :leave-pending="leaveTeamMutation.isPending.value"
        @create-team="createTeamMutation.mutate()"
        @join-by-token="joinByTokenMutation.mutate()"
        @leave-team="leaveTeamMutation.mutate($event)"
        @copy-token="copyToken"
        @refetch="refetch()"
      />
    </div>
  </AppLayout>
</template>