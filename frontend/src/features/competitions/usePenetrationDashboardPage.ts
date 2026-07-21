import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi, penetrationApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'

export interface PenetrationChallengeDto {
  id: string
  title: string
  typeId: string
  points?: number
}

export interface PenetrationNodeDto {
  id: string
  name: string
  role: string
  image: string
  isEntry: boolean
  isInternal: boolean
}

export interface PenetrationFlagDto {
  id: string
  name: string
  nodeName?: string | null
  stage: number
  score: number
  visible: boolean
  solved: boolean
  hintAfterSolved?: string | null
}

export interface PenetrationInstanceDto {
  id?: string | null
  status: string
  entryHost?: string | null
  entryPort?: number | null
  entryUrl?: string | null
  resetCount: number
  resetLimit: number
  expiresAt?: string | null
  cooldownUntil?: string | null
  lastError?: string | null
  ports?: Record<string, number>
}

export interface PenetrationDetailDto {
  topology?: {
    name: string
    description?: string | null
    nodes: PenetrationNodeDto[]
    flags: PenetrationFlagDto[]
    config?: { allowReset?: boolean }
  } | null
  instance: PenetrationInstanceDto
  totalStageCount: number
  solvedStageCount: number
  totalScore: number
}

export type PenetrationLifecycleAction = 'start' | 'stop' | 'reset' | 'destroy'

export interface PenetrationFlagResultDto {
  correct?: boolean
  alreadySolved?: boolean
  result?: string
  message?: string
}

// Canonical behavior follows V1's PenetrationWorkspace: the first penetration
// challenge is auto-selected, the detail polls every 15s, and both the
// lifecycle and flag mutations invalidate the detail plus leaderboard.
export function usePenetrationDashboardPage() {
  const route = useRoute()
  const router = useRouter()
  const queryClient = useQueryClient()

  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
  const hasValidCompetitionId = computed(() => competitionId.value.length > 0)
  const selectedChallengeId = ref('')
  const flag = ref('')

  const challengesQuery = useQuery({
    queryKey: computed(() => queryKeys.challenges(competitionId.value)),
    queryFn: () => competitionApi.challenges<PenetrationChallengeDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const penetrationChallenges = computed(() =>
    (challengesQuery.data.value ?? []).filter(challenge => challenge.typeId.toLowerCase().includes('penetration')),
  )

  watch(penetrationChallenges, (items) => {
    if (!items.some(item => item.id === selectedChallengeId.value))
      selectedChallengeId.value = items[0]?.id ?? ''
  }, { immediate: true })

  const detailKey = computed(() => ['penetration-detail', competitionId.value, selectedChallengeId.value])
  const detailQuery = useQuery({
    queryKey: detailKey,
    queryFn: () => penetrationApi.detail(competitionId.value, selectedChallengeId.value) as unknown as Promise<PenetrationDetailDto>,
    enabled: computed(() => Boolean(selectedChallengeId.value)),
    refetchInterval: 15_000,
  })

  function invalidateDetail() {
    void queryClient.invalidateQueries({ queryKey: detailKey.value })
    void queryClient.invalidateQueries({ queryKey: queryKeys.leaderboard(competitionId.value) })
  }

  const lifecycleMutation = useMutation({
    mutationFn: async (action: PenetrationLifecycleAction) => {
      const id = selectedChallengeId.value
      if (action === 'start') return penetrationApi.start(competitionId.value, id)
      if (action === 'stop') return penetrationApi.stop(competitionId.value, id)
      if (action === 'reset') return penetrationApi.reset(competitionId.value, id)
      return penetrationApi.destroy(competitionId.value, id)
    },
    onSuccess: () => {
      invalidateDetail()
    },
  })

  const submitFlagMutation = useMutation({
    mutationFn: () => penetrationApi.submitFlag(competitionId.value, selectedChallengeId.value, flag.value.trim()) as Promise<PenetrationFlagResultDto>,
    onSuccess: (result) => {
      if (result.correct) {
        flag.value = ''
        invalidateDetail()
      }
    },
  })

  const instance = computed(() => detailQuery.data.value?.instance)
  const entryAddress = computed(() => {
    const value = instance.value
    if (!value) return ''
    if (value.entryUrl) return value.entryUrl
    if (value.entryHost && value.entryPort) return `${value.entryHost}:${value.entryPort}`
    return value.entryHost ?? ''
  })

  function goCompetitionDetail() {
    void router.push({ name: 'competition-detail', params: { id: competitionId.value } })
  }

  return {
    competitionId,
    hasValidCompetitionId,
    selectedChallengeId,
    flag,
    penetrationChallenges,
    loadingChallenges: challengesQuery.isLoading,
    challengesError: challengesQuery.isError,
    challengesQueryError: challengesQuery.error,
    refetchChallenges: challengesQuery.refetch,
    detail: detailQuery.data,
    loadingDetail: detailQuery.isLoading,
    detailError: detailQuery.isError,
    detailQueryError: detailQuery.error,
    refetchDetail: detailQuery.refetch,
    invalidateDetail,
    lifecycleMutation,
    submitFlagMutation,
    instance,
    entryAddress,
    goCompetitionDetail,
  }
}
