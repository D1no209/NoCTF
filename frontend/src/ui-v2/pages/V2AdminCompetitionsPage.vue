<script setup lang="ts">
import { computed, ref } from 'vue'
import { useAdminCompetitionsPage, type AdminCompetitionDto } from '@/features/admin/useAdminCompetitionsPage'
import CommandAdminCompetitionsWorkspace, {
  type CommandAdminCompetition,
} from '../components/CommandAdminCompetitionsWorkspace.vue'

const {
  competitions: competitionData,
  isLoading,
  isError,
  error,
  refetch,
  form,
  canSave,
  saveMutation,
  deleteMutation,
  prepareCreate,
  goManage,
  goCollaborators,
  goViewPublic,
  goAwdpScreen,
} = useAdminCompetitionsPage()

const createDialogOpen = ref(false)
const deleteDialogOpen = ref(false)
const selectedCompetition = ref<CommandAdminCompetition | null>(null)
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const competitions = computed<CommandAdminCompetition[]>(() => (competitionData.value ?? [])
  .filter((competition): competition is AdminCompetitionDto & { id: string } => Boolean(competition.id))
  .map(competition => ({
    id: competition.id,
    title: competition.title?.trim() || 'Untitled competition',
    gameModeType: competition.gameModeType?.trim() || 'Ctf',
    status: competition.status?.trim() || 'Draft',
    startTime: competition.startTime ?? '',
    endTime: competition.endTime ?? '',
  })))

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

function openCreate() {
  prepareCreate()
  createDialogOpen.value = true
}

function openDelete(competition: CommandAdminCompetition) {
  selectedCompetition.value = competition
  deleteDialogOpen.value = true
}

function save() {
  saveMutation.mutate(undefined, {
    onSuccess: () => {
      createDialogOpen.value = false
      operationTone.value = 'success'
      operationMessage.value = 'Competition created.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Unable to save the competition.'
    },
  })
}

function remove() {
  const competition = selectedCompetition.value
  if (!competition)
    return
  deleteMutation.mutate(competition.id, {
    onSuccess: () => {
      deleteDialogOpen.value = false
      operationTone.value = 'success'
      operationMessage.value = 'Competition deleted.'
    },
    onError: (cause) => {
      operationTone.value = 'danger'
      operationMessage.value = cause instanceof Error ? cause.message : 'Unable to delete the competition.'
    },
  })
}

function manage(competition: CommandAdminCompetition) {
  goManage(competition)
}

function collaborators(competition: CommandAdminCompetition) {
  goCollaborators(competition)
}

function viewPublic(competition: CommandAdminCompetition) {
  goViewPublic(competition.id)
}

function awdpScreen(competition: CommandAdminCompetition) {
  goAwdpScreen(competition.id)
}

function retry() {
  void refetch()
}
</script>

<template>
  <CommandAdminCompetitionsWorkspace
    v-model:create-dialog-open="createDialogOpen"
    v-model:delete-dialog-open="deleteDialogOpen"
    v-model:form="form"
    :state="workspaceState"
    :error-message="errorMessage"
    :competitions="competitions"
    :selected-competition="selectedCompetition"
    :can-save="canSave"
    :save-pending="saveMutation.isPending.value"
    :delete-pending="deleteMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @retry="retry"
    @open-create="openCreate"
    @open-delete="openDelete"
    @save="save"
    @remove="remove"
    @manage="manage"
    @collaborators="collaborators"
    @view-public="viewPublic"
    @awdp-screen="awdpScreen"
  />
</template>
