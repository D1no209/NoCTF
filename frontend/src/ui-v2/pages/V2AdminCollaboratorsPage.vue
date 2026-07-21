<script setup lang="ts">
import { computed, ref } from 'vue'
import {
  useAdminCollaboratorsPage,
  type AdminCollaboratorCompetitionDto,
  type AdminCollaboratorDto,
  type AdminCollaboratorUserDto,
} from '@/features/admin/useAdminCollaboratorsPage'
import CommandAdminCollaboratorsWorkspace, {
  type CommandAdminCollaborator,
  type CommandAdminCollaboratorCompetition,
  type CommandAdminCollaboratorUser,
} from '../components/CommandAdminCollaboratorsWorkspace.vue'

const {
  selectedCompetitionId,
  competitions: competitionData,
  collaborators: collaboratorData,
  isLoading,
  isError,
  error,
  refetch,
  userSearchResults: userSearchData,
  newUserId,
  newUserSearch,
  newRole,
  addMutation,
  removeMutation,
} = useAdminCollaboratorsPage()

const addDialogOpen = ref(false)
const removeDialogOpen = ref(false)
const selectedCollaborator = ref<CommandAdminCollaborator | null>(null)
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const competitions = computed<CommandAdminCollaboratorCompetition[]>(() => (competitionData.value ?? [])
  .filter((competition): competition is AdminCollaboratorCompetitionDto & { id: string } => Boolean(competition.id))
  .map(competition => ({
    id: competition.id,
    title: competition.title?.trim() || 'Untitled competition',
  })))

const collaborators = computed<CommandAdminCollaborator[]>(() => (collaboratorData.value ?? [])
  .filter((collaborator): collaborator is AdminCollaboratorDto & { userId: string } => Boolean(collaborator.userId))
  .map(collaborator => ({
    userId: collaborator.userId,
    userName: collaborator.userName?.trim() || 'Unnamed user',
    role: collaborator.role?.trim() || 'Observer',
  })))

const userSearchResults = computed<CommandAdminCollaboratorUser[]>(() => (userSearchData.value ?? [])
  .filter((user): user is AdminCollaboratorUserDto & { id: string } => Boolean(user.id))
  .map(user => ({ id: user.id, userName: user.userName?.trim() || 'Unnamed user' })))

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const cause = error.value
  return cause instanceof Error ? cause.message : undefined
})

function openAdd() {
  addDialogOpen.value = true
}

function openRemove(collaborator: CommandAdminCollaborator) {
  selectedCollaborator.value = collaborator
  removeDialogOpen.value = true
}

function selectUser(user: CommandAdminCollaboratorUser) {
  newUserId.value = user.id
  newUserSearch.value = user.userName
}

function add() {
  addMutation.mutate(undefined, {
    onSuccess: () => {
      addDialogOpen.value = false
      operationTone.value = 'success'
      operationMessage.value = 'Collaborator added.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Unable to add the collaborator.'
    },
  })
}

function remove() {
  const collaborator = selectedCollaborator.value
  if (!collaborator)
    return
  removeMutation.mutate(collaborator.userId, {
    onSuccess: () => {
      removeDialogOpen.value = false
      operationTone.value = 'success'
      operationMessage.value = 'Collaborator removed.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Unable to remove the collaborator.'
    },
  })
}

function retry() {
  void refetch()
}
</script>

<template>
  <CommandAdminCollaboratorsWorkspace
    v-model:selected-competition-id="selectedCompetitionId"
    v-model:add-dialog-open="addDialogOpen"
    v-model:remove-dialog-open="removeDialogOpen"
    v-model:new-user-search="newUserSearch"
    v-model:new-role="newRole"
    :competitions="competitions"
    :collaborators="collaborators"
    :state="workspaceState"
    :error-message="errorMessage"
    :selected-collaborator="selectedCollaborator"
    :new-user-id="newUserId"
    :user-search-results="userSearchResults"
    :add-pending="addMutation.isPending.value"
    :remove-pending="removeMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @retry="retry"
    @open-add="openAdd"
    @open-remove="openRemove"
    @select-user="selectUser"
    @add="add"
    @remove="remove"
  />
</template>
