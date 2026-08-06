<script setup lang="ts">
import type { GlobalTeam } from '@/api/globalTeamPresentation'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  CalendarDays,
  Copy,
  Inbox,
  KeyRound,
  Loader2,
  LogOut,
  Pencil,
  Plus,
  RefreshCw,
  Save,
  UsersRound,
  X,
} from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { authApi, teamApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AppLayout from '@/components/layout/AppLayout.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import ErrorState from '@/components/state/ErrorState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import { Skeleton } from '@/components/ui/skeleton'

type TeamAction = 'create' | 'join' | null

const { locale, t } = useI18n()
const queryClient = useQueryClient()
const action = ref<TeamAction>(null)
const newTeamName = ref('')
const newAvatarUrl = ref('')
const invitationToken = ref('')
const editingTeamId = ref<string | null>(null)
const editTeamName = ref('')
const editAvatarUrl = ref('')

const {
  data: teams,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.myTeams,
  queryFn: teamApi.listMine,
})

const { data: currentUser } = useQuery({
  queryKey: queryKeys.currentUser,
  queryFn: authApi.getMe,
})

const teamList = computed(() => teams.value ?? [])

function openAction(nextAction: Exclude<TeamAction, null>) {
  action.value = action.value === nextAction ? null : nextAction
}

function closeAction() {
  action.value = null
  newTeamName.value = ''
  newAvatarUrl.value = ''
  invitationToken.value = ''
}

function refreshTeams() {
  void queryClient.invalidateQueries({ queryKey: queryKeys.myTeams })
}

const createTeamMutation = useMutation({
  mutationFn: () => teamApi.create({
    name: newTeamName.value.trim(),
    avatarUrl: newAvatarUrl.value.trim() || null,
  }),
  onSuccess: () => {
    refreshTeams()
    closeAction()
    toast.success(t('teams.createSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const joinTeamMutation = useMutation({
  mutationFn: () => teamApi.join(invitationToken.value.trim()),
  onSuccess: () => {
    refreshTeams()
    closeAction()
    toast.success(t('teams.joinSuccess'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

const leaveTeamMutation = useMutation({
  mutationFn: (teamId: string) => teamApi.leave(teamId),
  onSuccess: () => {
    refreshTeams()
    toast.success(t('teams.leaveSuccess'))
  },
  onError: () => toast.error(t('teams.leaveError')),
})

const rotateInvitationMutation = useMutation({
  mutationFn: (teamId: string) => teamApi.rotateInvitation(teamId),
  onSuccess: () => {
    refreshTeams()
    toast.success(t('teams.invitationRotated'))
  },
  onError: () => toast.error(t('teams.actionError')),
})

function isCaptain(team: GlobalTeam) {
  return currentUser.value?.userId === team.captainId
}

function startEditing(team: GlobalTeam) {
  editingTeamId.value = team.id
  editTeamName.value = team.name
  editAvatarUrl.value = team.avatarUrl ?? ''
}

function cancelEditing() {
  editingTeamId.value = null
  editTeamName.value = ''
  editAvatarUrl.value = ''
}

const updateTeamMutation = useMutation({
  mutationFn: (team: GlobalTeam) => teamApi.update(team.id, {
    name: editTeamName.value.trim(),
    avatarUrl: editAvatarUrl.value.trim() || null,
  }),
  onSuccess: () => {
    refreshTeams()
    cancelEditing()
    toast.success(t('teams.updateSuccess'))
  },
  onError: () => toast.error(t('errors.updateTeam')),
})

async function copyInvitation(token: string) {
  await navigator.clipboard.writeText(token)
  toast.success(t('teams.invitationCopied'))
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
              {{ t('teams.teamCount', { count: teamList.length }) }}
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
        <Skeleton v-for="index in 4" :key="index" class="h-64 border-2 border-border" />
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
                {{ action === 'create' ? t('teams.createGlobalDescription') : t('teams.joinGlobalDescription') }}
              </p>
            </div>
            <Button size="icon" variant="ghost" :aria-label="t('common.cancel')" @click="closeAction">
              <X class="size-4" />
            </Button>
          </div>

          <div v-if="action === 'create'" class="mt-5 grid gap-4 lg:grid-cols-2">
            <div class="space-y-2">
              <Label for="new-team-name">{{ t('teams.teamName') }}</Label>
              <Input
                id="new-team-name"
                v-model="newTeamName"
                :placeholder="t('teams.teamNamePlaceholder')"
                maxlength="128"
              />
            </div>
            <div class="space-y-2">
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
                :disabled="!newTeamName.trim() || createTeamMutation.isPending.value"
                @click="createTeamMutation.mutate()"
              >
                <Loader2 v-if="createTeamMutation.isPending.value" class="size-4 animate-spin" />
                <Plus v-else class="size-4" />
                {{ t('teams.createTeam') }}
              </Button>
            </div>
          </div>

          <div v-else class="mt-5 space-y-4">
            <div class="space-y-2">
              <Label for="team-invitation-token">{{ t('teams.invitationToken') }}</Label>
              <Input
                id="team-invitation-token"
                v-model="invitationToken"
                :placeholder="t('teams.tokenPlaceholder')"
                maxlength="32"
              />
            </div>
            <div class="flex justify-end gap-2">
              <Button variant="outline" @click="closeAction">
                {{ t('common.cancel') }}
              </Button>
              <Button
                :disabled="invitationToken.trim().length !== 32 || joinTeamMutation.isPending.value"
                @click="joinTeamMutation.mutate()"
              >
                <Loader2 v-if="joinTeamMutation.isPending.value" class="size-4 animate-spin" />
                <KeyRound v-else class="size-4" />
                {{ t('teams.joinByToken') }}
              </Button>
            </div>
          </div>
        </Card>

        <Card
          v-if="!teamList.length"
          class="flex min-h-64 flex-col items-center justify-center border-dashed px-6 py-10 text-center"
        >
          <Inbox class="size-9 text-muted-foreground" />
          <h2 class="mt-4 font-semibold">
            {{ t('teams.noTeams') }}
          </h2>
          <p class="mt-1 max-w-md text-sm text-muted-foreground">
            {{ t('teams.noTeamsUseHeader') }}
          </p>
        </Card>

        <div v-else class="grid gap-4 lg:grid-cols-2">
          <Card
            v-for="team in teamList"
            :key="team.id"
            class="flex h-full flex-col gap-5 p-5"
          >
            <div class="flex flex-wrap items-start justify-between gap-3">
              <div class="flex min-w-0 items-center gap-3">
                <img
                  v-if="team.avatarUrl"
                  :src="team.avatarUrl"
                  :alt="team.name"
                  class="size-12 border-2 border-border object-cover"
                >
                <div v-else class="grid size-12 shrink-0 place-items-center border-2 border-border bg-muted">
                  <UsersRound class="size-5 text-muted-foreground" />
                </div>
                <div class="min-w-0">
                  <p class="text-xs font-bold uppercase tracking-[0.14em] text-muted-foreground">
                    {{ t('teams.teamLabel') }}
                  </p>
                  <h2 class="mt-1 truncate text-lg font-bold">
                    {{ team.name }}
                  </h2>
                </div>
              </div>
              <Badge v-if="isCaptain(team)" variant="outline">
                {{ t('teams.captain') }}
              </Badge>
            </div>

            <Panel class="grid flex-1 gap-4 p-4 sm:grid-cols-2">
              <div>
                <p class="text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  {{ t('teams.members') }}
                </p>
                <p class="mt-1 font-semibold">
                  {{ t('teams.globalMemberCount', { count: team.memberCount }) }}
                </p>
              </div>
              <div>
                <p class="flex items-center gap-1.5 text-xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                  <CalendarDays class="size-3.5" />
                  {{ t('teams.createdAt') }}
                </p>
                <p class="mt-1 font-semibold">
                  {{ formatDate(team.createdAt) }}
                </p>
              </div>
            </Panel>

            <Panel v-if="isCaptain(team) && team.invitationToken" class="space-y-3 p-4">
              <div>
                <p class="text-xs font-bold uppercase tracking-[0.12em] text-muted-foreground">
                  {{ t('teams.invitationToken') }}
                </p>
                <p class="mt-2 break-all font-mono text-sm">
                  {{ team.invitationToken }}
                </p>
              </div>
              <div class="flex flex-wrap justify-end gap-2">
                <Button variant="outline" size="sm" @click="copyInvitation(team.invitationToken)">
                  <Copy class="size-4" />
                  {{ t('common.copy') }}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  :disabled="rotateInvitationMutation.isPending.value"
                  @click="rotateInvitationMutation.mutate(team.id)"
                >
                  <RefreshCw class="size-4" />
                  {{ t('teams.rotateInvitation') }}
                </Button>
              </div>
            </Panel>

            <Panel v-if="editingTeamId === team.id" class="space-y-4 p-4">
              <div class="grid gap-4 sm:grid-cols-2">
                <div class="space-y-2">
                  <Label :for="`edit-team-name-${team.id}`">{{ t('teams.teamName') }}</Label>
                  <Input :id="`edit-team-name-${team.id}`" v-model="editTeamName" maxlength="128" />
                </div>
                <div class="space-y-2">
                  <Label :for="`edit-team-avatar-${team.id}`">{{ t('teams.avatarUrl') }}</Label>
                  <Input
                    :id="`edit-team-avatar-${team.id}`"
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
                  @click="updateTeamMutation.mutate(team)"
                >
                  <Loader2 v-if="updateTeamMutation.isPending.value" class="size-4 animate-spin" />
                  <Save v-else class="size-4" />
                  {{ t('teams.saveChanges') }}
                </Button>
              </div>
            </Panel>

            <div class="flex flex-wrap justify-end gap-2">
              <Button
                v-if="isCaptain(team) && editingTeamId !== team.id"
                variant="outline"
                @click="startEditing(team)"
              >
                <Pencil class="size-4" />
                {{ t('teams.editTeam') }}
              </Button>
              <Button
                v-if="!isCaptain(team)"
                variant="outline"
                :disabled="leaveTeamMutation.isPending.value"
                @click="leaveTeamMutation.mutate(team.id)"
              >
                <Loader2 v-if="leaveTeamMutation.isPending.value" class="size-4 animate-spin" />
                <LogOut v-else class="size-4" />
                {{ t('teams.leave') }}
              </Button>
            </div>
          </Card>
        </div>
      </template>
    </div>
  </AppLayout>
</template>
