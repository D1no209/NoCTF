import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi, penetrationAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface OperationsChallengeDto {
  id: string
  title: string
  typeId: string
}

export interface PenetrationInstanceDto {
  id: string
  teamId?: string
  status?: string
  teamName?: string
  challengeId?: string
  challengeTitle?: string
  entryUrl?: string | null
  expiresAt?: string | null
  resetCount?: number
  lastError?: string | null
}

export interface PenetrationInstanceDetailDto extends PenetrationInstanceDto {
  entryHost?: string | null
  entryPort?: number | null
  containerIdsJson?: string | null
  portMappingsJson?: string | null
}

export function useAdminCompetitionOperationsPage() {
  const route = useRoute()
  const queryClient = useQueryClient()
  const competitionId = computed(() => String(route.params.id))
  const selectedChallengeId = ref('')
  const topologyJson = ref('{}')
  const instanceChallengeFilter = ref('__all__')
  const selectedInstanceId = ref('')

  const challengesQuery = useQuery({
    queryKey: computed(() => queryKeys.adminCompetitionChallenges(competitionId.value)),
    queryFn: () => adminApi.competitionChallenges<OperationsChallengeDto[]>(competitionId.value),
  })

  const penetrationChallenges = computed(() => (challengesQuery.data.value ?? [])
    .filter(item => item.typeId.toLowerCase().includes('penetration')))

  watch(penetrationChallenges, (items) => {
    if (!items.some(item => item.id === selectedChallengeId.value))
      selectedChallengeId.value = items[0]?.id ?? ''
  }, { immediate: true })

  const topologyKey = computed(() => ['penetration-topology', competitionId.value, selectedChallengeId.value])
  const topologyQuery = useQuery({
    queryKey: topologyKey,
    queryFn: () => penetrationAdminApi.competitionTopology(competitionId.value, selectedChallengeId.value) as unknown as Promise<unknown>,
    enabled: computed(() => Boolean(selectedChallengeId.value)),
  })

  watch(topologyQuery.data, (value) => {
    if (value)
      topologyJson.value = JSON.stringify(value, null, 2)
  }, { immediate: true })

  const instancesKey = computed(() => ['penetration-instances', competitionId.value])
  const instancesQuery = useQuery({
    queryKey: computed(() => [...instancesKey.value, instanceChallengeFilter.value]),
    queryFn: () => penetrationAdminApi.instances(competitionId.value, {
      challengeId: instanceChallengeFilter.value === '__all__' ? undefined : instanceChallengeFilter.value,
    } as never) as unknown as Promise<PenetrationInstanceDto[] | { items?: PenetrationInstanceDto[] }>,
    refetchInterval: 15_000,
  })

  const instances = computed(() => Array.isArray(instancesQuery.data.value)
    ? instancesQuery.data.value
    : instancesQuery.data.value?.items ?? [])

  const instanceDetailQuery = useQuery({
    queryKey: computed(() => ['penetration-instance-detail', competitionId.value, selectedInstanceId.value]),
    queryFn: () => penetrationAdminApi.instance(competitionId.value, selectedInstanceId.value) as unknown as Promise<PenetrationInstanceDetailDto>,
    enabled: computed(() => Boolean(selectedInstanceId.value)),
  })

  const saveTopologyMutation = useMutation({
    mutationFn: () => penetrationAdminApi.updateCompetitionTopology(
      competitionId.value,
      selectedChallengeId.value,
      JSON.parse(topologyJson.value),
    ),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: topologyKey.value })
    },
  })

  const instanceActionMutation = useMutation({
    mutationFn: ({ id, action }: { id: string, action: 'reset' | 'destroy' }) =>
      action === 'reset'
        ? penetrationAdminApi.resetInstance(competitionId.value, id)
        : penetrationAdminApi.destroyInstance(competitionId.value, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: instancesKey.value })
    },
  })

  return {
    competitionId,
    selectedChallengeId,
    topologyJson,
    instanceChallengeFilter,
    selectedInstanceId,
    penetrationChallenges,
    loadingChallenges: challengesQuery.isLoading,
    loadingTopology: topologyQuery.isLoading,
    topologyError: topologyQuery.isError,
    refetchTopology: topologyQuery.refetch,
    instances,
    loadingInstances: instancesQuery.isLoading,
    refetchInstances: instancesQuery.refetch,
    selectedInstance: instanceDetailQuery.data,
    loadingInstanceDetail: instanceDetailQuery.isLoading,
    saveTopologyMutation,
    instanceActionMutation,
  }
}
