<script setup lang="ts">
import { computed, ref } from 'vue'
import CommandAdminChallengesWorkspace, {
  type CommandAdminChallenge,
} from '../components/CommandAdminChallengesWorkspace.vue'
import type { ChallengeTemplateDto, ChallengeTemplateSubmit } from '@/features/admin/challengeTemplate'
import { useAdminChallengesPage } from '@/features/admin/useAdminChallengesPage'

const editDialogOpen = ref(false)
const deleteDialogOpen = ref(false)
const revealDialogOpen = ref(false)
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const {
  templates,
  isLoading,
  isError,
  error,
  refetch,
  selectedTemplate,
  revealedSecret,
  saveMutation,
  deleteMutation,
  revealMutation,
  goCreateChallenge,
} = useAdminChallengesPage()

const challenges = computed<CommandAdminChallenge[]>(() => (templates.value ?? [])
  .filter((challenge): challenge is ChallengeTemplateDto & { id: string } => Boolean(challenge.id)))

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value)
    return 'loading'
  if (isError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const value = error.value
  return value instanceof Error ? value.message : undefined
})

function reportSuccess(message: string) {
  operationTone.value = 'success'
  operationMessage.value = message
}

function reportError(error: unknown, fallback: string) {
  operationTone.value = 'danger'
  operationMessage.value = error instanceof Error ? error.message : fallback
}

function openEdit(challenge: CommandAdminChallenge) {
  selectedTemplate.value = challenge
  editDialogOpen.value = true
}

function openDelete(challenge: CommandAdminChallenge) {
  selectedTemplate.value = challenge
  deleteDialogOpen.value = true
}

function openReveal(challenge: CommandAdminChallenge) {
  selectedTemplate.value = challenge
  revealedSecret.value = ''
  revealDialogOpen.value = true
}

function save(value: ChallengeTemplateSubmit) {
  saveMutation.mutate(value, {
    onSuccess: () => {
      editDialogOpen.value = false
      reportSuccess('Challenge template updated.')
    },
    onError: error => reportError(error, 'Unable to save the challenge template.'),
  })
}

function remove() {
  deleteMutation.mutate(selectedTemplate.value!.id, {
    onSuccess: () => {
      deleteDialogOpen.value = false
      reportSuccess('Challenge template deleted.')
    },
    onError: error => reportError(error, 'Unable to delete the challenge template.'),
  })
}

function reveal() {
  revealMutation.mutate(selectedTemplate.value!.id, {
    onError: error => reportError(error, 'Unable to reveal the flag secret.'),
  })
}

function retry() {
  void refetch()
}
</script>

<template>
  <CommandAdminChallengesWorkspace
    v-model:edit-dialog-open="editDialogOpen"
    v-model:delete-dialog-open="deleteDialogOpen"
    v-model:reveal-dialog-open="revealDialogOpen"
    :state="workspaceState"
    :error-message="errorMessage"
    :challenges="challenges"
    :selected-challenge="selectedTemplate"
    :revealed-secret="revealedSecret"
    :save-pending="saveMutation.isPending.value"
    :delete-pending="deleteMutation.isPending.value"
    :reveal-pending="revealMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @retry="retry"
    @create="goCreateChallenge"
    @open-edit="openEdit"
    @open-delete="openDelete"
    @open-reveal="openReveal"
    @save="save"
    @remove="remove"
    @reveal="reveal"
  />
</template>
