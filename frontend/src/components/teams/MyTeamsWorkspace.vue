<script setup lang="ts">
import type { PublicTeam } from '@/api/teamPresentation'
import type { MyTeamRegistration } from '@/components/teams/myTeamRegistrations'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  CalendarDays,
  Inbox,
  KeyRound,
  Loader2,
  LockKeyhole,
  Pencil,
  Plus,
  Save,
  UsersRound,
  X,
} from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { authApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { isPlatformAdministrator } from '@/api/userRole'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { loadMyTeamsWorkspace } from '@/components/teams/myTeamRegistrations'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'

type TeamAction = 'create' | 'join' | null

const { locale, t } = useI18n()
const queryClient = useQueryClient()
const action = ref<TeamAction>(null)
const selectedCompetitionId = ref('')
const newTeamName = ref('')
const newAvatarUrl = ref('')
const invitationToken = ref('')
const editingTeamId = ref<string | null>(null)
const editTeamName = ref('')
const editAvatarUrl = ref('')

const {
  data: workspace,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: () => loadMyTeamsWorkspace(),
})

const { data: currentUser } = useQuery({
  queryKey: queryKeys.currentUser,
  queryFn: () => authApi.getMe(),
})

const registrations = computed(() => workspace.value?.registrations ?? [])
const availableCompetitions = computed(() => workspace.value?.availableCompetitions ?? [])

function openAction(nextAction: Exclude<TeamAction, null>) {
  action.value = action.value === nextAction ? null : nextAction
  if (action.value && !availableCompetitions.value.some(
    competition => competition.id === selectedCompetitionId.value,
  )) {
    selectedCompetitionId.value = availableCompetitions.value[0]?.id ?? ''
  }
}

function closeAction() {
  action.value = null
  newTeamName.value = ''
  newAvatarUrl.value = ''
  invitationToken.value = ''
}

function selectedCompetition() {
  const competition = availableCompetitions.value.find(
    candidate => candidate.id === selectedCompetitionId.value,
  )
  if (!competition)
    throw new TypeError('A competition must be selected before changing team membership.')
  return competition
}

function refreshTeams(competitionId: string) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
  void queryClient.invalidateQueries({ queryKey: queryKeys.myCompetitionTeam(competitionId) })
  void queryClient.invalidateQueries({ queryKey: queryKeys.teams(competitionId) })
}

const createTeamMutation = useMutation({
  mutationFn: () => {
    const competition = selectedCompetition()
    return teamApi.create(competition.id, {
      name: newTeamName.value.trim(),
      avatarUrl: newAvatarUrl.value.trim() || null,
    })
  },
  onSuccess: (team) => {
    refreshTeams(team.competitionId)
    closeAction()
    toast.success(t('teams.createSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const joinTeamMutation = useMutation({
  mutationFn: async () => {
    const competition = selectedCompetition()
    await teamApi.join(competition.id, invitationToken.value.trim())
    return competition.id
  },
  onSuccess: (competitionId) => {
    refreshTeams(competitionId)
    closeAction()
    toast.success(t('teams.joinSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

function isCaptain(team: PublicTeam) {
  return currentUser.value?.userId === team.captainId
}

function canManageTeam(team: PublicTeam) {
  return isCaptain(team) || isPlatformAdministrator(currentUser.value?.role)
}

function startEditing(registration: MyTeamRegistration) {
  editingTeamId.value = registration.team.id
  editTeamName.value = registration.team.name
  editAvatarUrl.value = registration.team.avatarUrl ?? ''
}

function cancelEditing() {
  editingTeamId.value = null
  editTeamName.value = ''
  editAvatarUrl.value = ''
}

const updateTeamMutation = useMutation({
  mutationFn: (registration: MyTeamRegistration) => teamApi.update(
    registration.competition.id,
    registration.team.id,
    {
      name: editTeamName.value.trim(),
      avatarUrl: editAvatarUrl.value.trim() || null,
    },
  ),
  onSuccess: (team) => {
    refreshTeams(team.competitionId)
    cancelEditing()
    toast.success(t('teams.updateSuccess'))
  },
  onError: () => toast.error(t('errors.updateTeam')),
})

function teamStatusVariant(status: 'pending' | 'approved' | 'rejected') {
  if (status === 'approved')
    return 'default'
  if (status === 'rejected')
    return 'destructive'
  return 'secondary'
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1400px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
      <PageHeader
        :title="t('teams.workspaceTitle')"
        :description="t('teams.workspaceDescription')"
      >
        <template #actions>
          <div class="flex flex-wrap items-center justify-end gap-2">
            <Badge variant="outline">
              {{ t('teams.teamCount', { count: registrations.length }) }}
            </Badge>
            <Button size="sm" @click="openAction('create')">
              <Plus class="size-4" />
              {{ t('teams.createTeam') }}
            </Button>
            <Button size="sm" variant="outline" @click="openAction('join')">
              <KeyRound class="size-4" />
              {{ t('teams.joinByToken') }}
            </Button>
          </div>
        </template>
      </PageHeader>

      <div v-if="isLoading" class="grid gap-4 lg:grid-cols-2">
        <Skeleton v-for="index in 4" :key="index" class="h-48 border-2 border-border" />
      </div>

      <ErrorState
        v-else-if="isError"
        :title="t('errors.loadMyTeams')"
        :retry-label="t('common.refresh')"
        @retry="refetch()"
      />

      <template v-else>
        <Card v-if="action" class="p-5">
          <div class="flex items-start justify-between gap-4">
            <div>
              <h2 class="font-bold">
                {{ action === 'create' ? t('teams.createPanelTitle') : t('teams.joinPanelTitle') }}
              </h2>
              <p class="mt-1 text-sm text-muted-foreground">
                {{ action === 'create' ? t('teams.createPanelDescription') : t('teams.joinPanelDescription') }}
              </p>
            </div>
            <Button size="icon" variant="ghost" :aria-label="t('common.cancel')" @click="closeAction">
              <X class="size-4" />
            </Button>
          </div>

          <div v-if="availableCompetitions.length" class="mt-5 grid gap-4 lg:grid-cols-2">
            <div class="space-y-2">
              <Label for="team-competition">{{ t('teams.competition') }}</Label>
              <Select v-model="selectedCompetitionId">
                <SelectTrigger id="team-competition">
                  <SelectValue :placeholder="t('teams.selectCompetition')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem
                    v-for="competition in availableCompetitions"
                    :key="competition.id"
                    :value="competition.id"
                  >
                    {{ competition.title }} · {{ competition.mode.toUpperCase() }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>

            <template v-if="action === 'create'">
              <div class="space-y-2">
                <Label for="new-team-name">{{ t('teams.teamName') }}</Label>
                <Input
                  id="new-team-name"
                  v-model="newTeamName"
                  :placeholder="t('teams.teamNamePlaceholder')"
                  maxlength="128"
                />
              </div>
              <div class="space-y-2 lg:col-span-2">
                <Label for="new-team-avatar">{{ t('teams.avatarUrl') }}</Label>
                <Input
                  id="new-team-avatar"
                  v-model="newAvatarUrl"
                  type="url"
                  :placeholder="t('teams.avatarUrlPlaceholder')"
                />
              </div>
              <div class="flex justify-end gap-2 lg:col-span-2">
                <Button variant="outline" @click="closeAction">
                  {{ t('common.cancel') }}
                </Button>
                <Button
                  :disabled="!selectedCompetitionId || !newTeamName.trim() || createTeamMutation.isPending.value"
                  @click="createTeamMutation.mutate()"
                >
                  <Loader2 v-if="createTeamMutation.isPending.value" class="size-4 animate-spin" />
                  <Plus v-else class="size-4" />
                  {{ t('teams.createTeam') }}
                </Button>
              </div>
            </template>

            <template v-else>
              <div class="space-y-2">
                <Label for="team-invitation-token">{{ t('teams.invitationToken') }}</Label>
                <Input
                  id="team-invitation-token"
                  v-model="invitationToken"
                  :placeholder="t('teams.tokenPlaceholder')"
                  maxlength="32"
                />
              </div>
              <div class="flex justify-end gap-2 lg:col-span-2">
                <Button variant="outline" @click="closeAction">
                  {{ t('common.cancel') }}
                </Button>
                <Button
                  :disabled="!selectedCompetitionId || !invitationToken.trim() || joinTeamMutation.isPending.value"
                  @click="joinTeamMutation.mutate()"
                >
                  <Loader2 v-if="joinTeamMutation.isPending.value" class="size-4 animate-spin" />
                  <KeyRound v-else class="size-4" />
                  {{ t('teams.joinByToken') }}
                </Button>
              </div>
            </template>
          </div>

          <Panel v-else class="mt-5 border-dashed p-4 text-sm text-muted-foreground">
            {{ t('teams.noAvailableCompetitions') }}
          </Panel>
        </Card>

        <Card
          v-if="!registrations.length"
          class="flex min-h-64 flex-col items-center justify-center border-dashed px-6 py-10 text-center"
        >
          <Inbox class="size-9 text-muted-foreground" />
          <h2 class="mt-4 font-semibold">
            {{ t('teams.noTeams') }}
          </h2>
          <p class="mt-1 max-w-md text-sm text-muted-foreground">
            {{ t('teams.noTeamsDescription') }}
          </p>
          <Button class="mt-5" @click="openAction('create')">
            <Plus class="size-4" />
            {{ t('teams.createTeam') }}
          </Button>
        </Card>

        <div v-else class="grid gap-4 lg:grid-cols-2">
          <Card
            v-for="item in registrations"
            :key="item.team.id"
            class="flex h-full flex-col gap-5 p-5"
          >
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div class="flex min-w-0 items-center gap-3">
                <img
                  v-if="item.team.avatarUrl"
                  :src="item.team.avatarUrl"
                  :alt="item.team.name"
                  class="size-12 border-2 border-border object-cover"
                >
                <div v-else class="grid size-12 shrink-0 place-items-center border-2 border-border bg-muted">
                  <UsersRound class="size-5 text-muted-foreground" />
                </div>
                <div class="min-w-0">
                  <div class="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                    {{ t('teams.teamLabel') }}
                  </div>
                  <h2 class="mt-1 truncate text-lg font-bold">
                    {{ item.team.name }}
                  </h2>
                </div>
              </div>
              <div class="flex flex-wrap items-center justify-end gap-2">
                <Badge v-if="isCaptain(item.team)" variant="outline">
                  {{ t('teams.captain') }}
                </Badge>
                <Badge :variant="teamStatusVariant(item.team.registrationStatus)">
                  {{ t(`teams.status.${item.team.registrationStatus}`) }}
                </Badge>
              </div>
            </div>

            <Panel class="grid flex-1 gap-4 p-4 sm:grid-cols-2">
              <div>
                <p class="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  {{ t('teams.competition') }}
                </p>
                <div class="mt-1 flex flex-wrap items-center gap-2">
                  <p class="font-semibold">
                    {{ item.competition.title }}
                  </p>
                  <Badge variant="outline">
                    {{ item.competition.mode.toUpperCase() }}
                  </Badge>
                </div>
              </div>
              <div>
                <p class="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  {{ t('teams.members') }}
                </p>
                <p class="mt-1 font-semibold">
                  {{ t('teams.memberSummary', {
                    current: item.team.memberCount,
                    maximum: item.competition.maxTeamMembers,
                  }) }}
                </p>
              </div>
              <div>
                <p class="flex items-center gap-1.5 text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  <CalendarDays class="size-3.5" />
                  {{ t('teams.registeredAt') }}
                </p>
                <p class="mt-1 font-semibold">
                  {{ formatDate(item.team.registeredAt) }}
                </p>
              </div>
              <div>
                <p class="flex items-center gap-1.5 text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  <LockKeyhole class="size-3.5" />
                  {{ t('teams.rosterState') }}
                </p>
                <p class="mt-1 font-semibold">
                  {{ item.team.isLocked ? t('teams.locked') : t('teams.unlocked') }}
                </p>
              </div>
            </Panel>

            <Panel v-if="editingTeamId === item.team.id" class="space-y-4 p-4">
              <div class="grid gap-4 sm:grid-cols-2">
                <div class="space-y-2">
                  <Label :for="`edit-team-name-${item.team.id}`">{{ t('teams.teamName') }}</Label>
                  <Input
                    :id="`edit-team-name-${item.team.id}`"
                    v-model="editTeamName"
                    maxlength="128"
                  />
                </div>
                <div class="space-y-2">
                  <Label :for="`edit-team-avatar-${item.team.id}`">{{ t('teams.avatarUrl') }}</Label>
                  <Input
                    :id="`edit-team-avatar-${item.team.id}`"
                    v-model="editAvatarUrl"
                    type="url"
                    :placeholder="t('teams.avatarUrlPlaceholder')"
                  />
                </div>
              </div>
              <div class="flex justify-end gap-2">
                <Button variant="outline" @click="cancelEditing">
                  {{ t('common.cancel') }}
                </Button>
                <Button
                  :disabled="!editTeamName.trim() || updateTeamMutation.isPending.value"
                  @click="updateTeamMutation.mutate(item)"
                >
                  <Loader2 v-if="updateTeamMutation.isPending.value" class="size-4 animate-spin" />
                  <Save v-else class="size-4" />
                  {{ t('teams.saveChanges') }}
                </Button>
              </div>
            </Panel>

            <Button
              v-if="canManageTeam(item.team) && editingTeamId !== item.team.id"
              variant="outline"
              @click="startEditing(item)"
            >
              <Pencil class="size-4" />
              {{ t('teams.editTeam') }}
            </Button>
          </Card>
        </div>
      </template>
    </div>
  </AppLayout>
</template>
