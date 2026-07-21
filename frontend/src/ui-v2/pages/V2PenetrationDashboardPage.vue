<script setup lang="ts">
import { computed, ref } from 'vue'
import type { CommandAwdChallenge } from '../components/awd-contract'
import CommandPenetrationWorkspace from '../components/CommandPenetrationWorkspace.vue'
import {
  usePenetrationDashboardPage,
  type PenetrationFlagResultDto,
  type PenetrationLifecycleAction,
} from '@/features/competitions/usePenetrationDashboardPage'

const {
  hasValidCompetitionId,
  selectedChallengeId,
  flag,
  penetrationChallenges,
  loadingChallenges,
  challengesError,
  challengesQueryError,
  refetchChallenges,
  detail,
  loadingDetail,
  detailQueryError,
  refetchDetail,
  lifecycleMutation: actionMutation,
  submitFlagMutation: submitMutation,
  goCompetitionDetail,
} = usePenetrationDashboardPage()

const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const challenges = computed<CommandAwdChallenge[]>(() => penetrationChallenges.value
  .filter((challenge): challenge is typeof challenge & { id: string } => Boolean(challenge.id))
  .map(challenge => ({ id: challenge.id, title: challenge.title?.trim() || 'Untitled challenge' })))

const workspaceState = computed<'invalid' | 'loading' | 'error' | 'ready'>(() => {
  if (!hasValidCompetitionId.value)
    return 'invalid'
  if (loadingChallenges.value)
    return 'loading'
  if (challengesError.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => {
  const error = challengesQueryError.value ?? detailQueryError.value
  return error instanceof Error ? error.message : undefined
})

function reportSuccess(message: string) {
  operationTone.value = 'success'
  operationMessage.value = message
}

function reportError(error: unknown, fallback: string) {
  operationTone.value = 'danger'
  operationMessage.value = error instanceof Error ? error.message : fallback
}

function runAction(name: PenetrationLifecycleAction) {
  actionMutation.mutate(name, {
    onSuccess: () => reportSuccess(`Instance ${name} accepted. Range state has been refreshed.`),
    onError: error => reportError(error, 'The instance action failed.'),
  })
}

function submit() {
  submitMutation.mutate(undefined, {
    onSuccess: (result) => {
      const flagResult = result as PenetrationFlagResultDto | undefined
      if (flagResult?.correct)
        reportSuccess(flagResult.message || 'Flag accepted. Range progress has been refreshed.')
      else
        reportError(null, flagResult?.message || 'The server rejected this flag.')
    },
    onError: error => reportError(error, 'Flag submission failed.'),
  })
}

function retry() {
  void refetchChallenges()
  void refetchDetail()
}
</script>

<template>
  <CommandPenetrationWorkspace
    v-model:selected-challenge-id="selectedChallengeId"
    v-model:flag="flag"
    :state="workspaceState"
    :error-message="errorMessage"
    :challenges="challenges"
    :detail="detail"
    :detail-loading="loadingDetail"
    :action-pending="actionMutation.isPending.value ? actionMutation.variables.value ?? '' : ''"
    :submit-pending="submitMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @back="goCompetitionDetail"
    @retry="retry"
    @action="runAction"
    @submit="submit"
  />
</template>
