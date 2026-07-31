<script setup lang="ts">
import type { PublicCompetition } from '@/api/competitionPresentation'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Users } from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { competitionApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import TeamsWorkspace from '@/components/teams/TeamsWorkspace.vue'
import { Button } from '@/components/ui/button'

const { t } = useI18n()
const queryClient = useQueryClient()
const joinToken = ref('')
const newTeamName = ref('')
const selectedCompetitionId = ref('')

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
}

const { data: teams, isLoading, isError, refetch } = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => teamApi.mine<MyTeam[]>(),
})

const { data: competitions, isLoading: loadingCompetitions } = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list(),
})

const activeTeams = computed(() => (teams.value ?? []).filter(team => !team.isBanned))
const bannedTeams = computed(() => (teams.value ?? []).filter(team => team.isBanned))
const availableCompetitions = computed<PublicCompetition[]>(() => competitions.value ?? [])
const canCreateTeam = computed(() => {
  if (!selectedCompetitionId.value || !newTeamName.value.trim())
    return false
  return true
})

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create<MyTeam>({
    competitionId: selectedCompetitionId.value,
    name: newTeamName.value.trim(),
  }),
  onSuccess: (team) => {
    newTeamName.value = ''
    queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
    queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeams(team.competitionId) })
    toast.success(t('teams.createSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
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
        v-model:new-team-name="newTeamName"
        v-model:join-token="joinToken"
        :teams="teams ?? []"
        :active-teams="activeTeams"
        :banned-teams="bannedTeams"
        :is-loading="isLoading"
        :is-error="isError"
        :loading-competitions="loadingCompetitions"
        :available-competitions="availableCompetitions"
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
