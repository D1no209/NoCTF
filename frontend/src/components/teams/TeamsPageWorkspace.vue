<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { toast } from 'vue-sonner'
import { teamApi, competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import { Button } from '@/components/ui/button'
import { Users } from 'lucide-vue-next'
import TeamsWorkspace from '@/components/teams/TeamsWorkspace.vue'

const { t } = useI18n()
const queryClient = useQueryClient()
const joinToken = ref('')
const newTeamName = ref('')
const selectedCompetitionId = ref('')
const selectedTrackName = ref('')

interface MyTeam {
  id: string
  competitionId: string
  name: string
  inviteToken: string
  registrationStatus: string
  competitionTitle: string
  competitionStatus: string
  gameModeType: string
  memberCount: number
  maxTeamMembers: number
  isCaptain: boolean
  isLocked: boolean
  isBanned: boolean
  trackName?: string | null
}

interface CompetitionListItem {
  id: string
  title: string
  status: string
  gameModeType: string
}

interface CompetitionDetail {
  id: string
  title: string
  tracksEnabled: boolean
  trackNames: string[]
}

const { data: teams, isLoading, isError, refetch } = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => teamApi.mine<MyTeam[]>(),
})

const { data: competitions, isLoading: loadingCompetitions } = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list<CompetitionListItem[]>(),
})

const { data: selectedCompetitionDetail, isLoading: loadingSelectedCompetition } = useQuery({
  queryKey: computed(() => queryKeys.competition(selectedCompetitionId.value)),
  queryFn: () => competitionApi.get<CompetitionDetail>(selectedCompetitionId.value),
  enabled: computed(() => !!selectedCompetitionId.value),
})

const activeTeams = computed(() => (teams.value ?? []).filter(team => !team.isBanned))
const bannedTeams = computed(() => (teams.value ?? []).filter(team => team.isBanned))
const availableCompetitions = computed(() => competitions.value ?? [])
const selectedCompetitionRequiresTrack = computed(() => Boolean(selectedCompetitionDetail.value?.tracksEnabled))
const selectedCompetitionTracks = computed(() => selectedCompetitionDetail.value?.trackNames ?? [])
const canCreateTeam = computed(() => {
  if (!selectedCompetitionId.value || !newTeamName.value.trim()) return false
  if (selectedCompetitionRequiresTrack.value && !selectedTrackName.value) return false
  return true
})

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create<MyTeam>({
    competitionId: selectedCompetitionId.value,
    name: newTeamName.value.trim(),
    trackName: selectedCompetitionRequiresTrack.value ? selectedTrackName.value : undefined,
  }),
  onSuccess: (team) => {
    newTeamName.value = ''
    selectedTrackName.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
    toast.success(t('teams.createSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

watch(selectedCompetitionId, () => {
  selectedTrackName.value = ''
})

const joinByTokenMutation = useMutation({
  mutationFn: () => teamApi.joinByToken<MyTeam>(joinToken.value.trim()),
  onSuccess: (team) => {
    joinToken.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
    toast.success(t('teams.joinSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const leaveTeamMutation = useMutation({
  mutationFn: (teamId: string) => teamApi.leave(teamId),
  onSuccess: () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    toast.success(t('teams.leaveSuccess'))
  },
  onError: () => toast.error(t('teams.leaveError')),
})

async function copyToken(token: string) {
  await navigator.clipboard.writeText(token)
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
        :teams="teams ?? []"
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