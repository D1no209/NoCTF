<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { useRoute, useRouter } from 'vue-router'
import type { NoCtfapiEndpointsCompetitionsChallengeDto } from '@/api/generated/types.gen'
import { competitionApi, penetrationApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import type { CommandAwdChallenge } from '../components/awd-contract'
import CommandPenetrationWorkspace, { type CommandPenetrationDetail } from '../components/CommandPenetrationWorkspace.vue'

const guidPattern = /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i

const route = useRoute()
const router = useRouter()
const queryClient = useQueryClient()
const selectedChallengeId = ref('')
const flag = ref('')
const operationMessage = ref('')
const operationTone = ref<'success' | 'danger'>('success')

const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
const hasValidCompetitionId = computed(() => guidPattern.test(competitionId.value))

const challengesQuery = useQuery({
  queryKey: computed(() => queryKeys.challenges(competitionId.value)),
  queryFn: () => competitionApi.challenges<NoCtfapiEndpointsCompetitionsChallengeDto[]>(competitionId.value),
  enabled: hasValidCompetitionId,
})

const challenges = computed<CommandAwdChallenge[]>(() => (challengesQuery.data.value ?? [])
  .filter((challenge): challenge is NoCtfapiEndpointsCompetitionsChallengeDto & { id: string } =>
    Boolean(challenge.id && challenge.typeId?.toLowerCase().includes('penetration')),
  )
  .map(challenge => ({ id: challenge.id, title: challenge.title?.trim() || 'Untitled challenge' })))

watch(challenges, (items) => {
  if (!items.some(item => item.id === selectedChallengeId.value))
    selectedChallengeId.value = items[0]?.id ?? ''
}, { immediate: true })

const detailKey = computed(() => ['penetration-detail', competitionId.value, selectedChallengeId.value] as const)
const detailQuery = useQuery({
  queryKey: detailKey,
  queryFn: () => penetrationApi.detail(competitionId.value, selectedChallengeId.value) as unknown as Promise<CommandPenetrationDetail>,
  enabled: computed(() => hasValidCompetitionId.value && Boolean(selectedChallengeId.value)),
  refetchInterval: 15_000,
})

const workspaceState = computed<'invalid' | 'loading' | 'error' | 'ready'>(() => {
  if (!hasValidCompetitionId.value)
    return 'invalid'
  if (challengesQuery.isLoading.value)
    return 'loading'
  if (challengesQuery.isError.value)
    return 'error'
  return 'ready'
})
const errorMessage = computed(() => {
  const error = challengesQuery.error.value ?? detailQuery.error.value
  return error instanceof Error ? error.message : undefined
})

function refreshDetail() {
  return queryClient.invalidateQueries({ queryKey: detailKey.value })
}

const actionMutation = useMutation({
  mutationFn: async (name: 'start' | 'stop' | 'reset' | 'destroy') => {
    const id = selectedChallengeId.value
    if (!id)
      throw new Error('Select a penetration challenge first.')
    if (name === 'start')
      return penetrationApi.start(competitionId.value, id)
    if (name === 'stop')
      return penetrationApi.stop(competitionId.value, id)
    if (name === 'reset')
      return penetrationApi.reset(competitionId.value, id)
    return penetrationApi.destroy(competitionId.value, id)
  },
  onSuccess: async (_result, name) => {
    operationTone.value = 'success'
    operationMessage.value = `Instance ${name} accepted. Range state has been refreshed.`
    await refreshDetail()
  },
  onError: (error) => {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'The instance action failed.'
  },
})

const submitMutation = useMutation({
  mutationFn: async () => {
    const challengeId = selectedChallengeId.value.trim()
    const submittedFlag = flag.value.trim()
    if (!challengeId || !submittedFlag)
      throw new Error('A challenge and flag value are required.')
    return penetrationApi.submitFlag(competitionId.value, challengeId, submittedFlag) as unknown as Promise<{ correct?: boolean, message?: string }>
  },
  onSuccess: async (result) => {
    if (result?.correct) {
      flag.value = ''
      operationTone.value = 'success'
      operationMessage.value = result.message || 'Flag accepted. Range progress has been refreshed.'
      await Promise.all([
        refreshDetail(),
        queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) }),
      ])
      return
    }
    operationTone.value = 'danger'
    operationMessage.value = result?.message || 'The server rejected this flag.'
  },
  onError: (error) => {
    operationTone.value = 'danger'
    operationMessage.value = error instanceof Error ? error.message : 'Flag submission failed.'
  },
})

function backToCompetition() {
  router.push({ name: 'competition-detail', params: { id: competitionId.value } })
}

function retry() {
  void challengesQuery.refetch()
  void detailQuery.refetch()
}
</script>

<template>
  <CommandPenetrationWorkspace
    v-model:selected-challenge-id="selectedChallengeId"
    v-model:flag="flag"
    :state="workspaceState"
    :error-message="errorMessage"
    :challenges="challenges"
    :detail="detailQuery.data.value"
    :detail-loading="detailQuery.isLoading.value"
    :action-pending="actionMutation.isPending.value ? actionMutation.variables.value ?? '' : ''"
    :submit-pending="submitMutation.isPending.value"
    :operation-message="operationMessage"
    :operation-tone="operationTone"
    @back="backToCompetition"
    @retry="retry"
    @action="actionMutation.mutate($event)"
    @submit="submitMutation.mutate()"
  />
</template>
