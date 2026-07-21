import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { useSignalR, type AttackLogDto } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

export interface AwdServiceStatusDto {
  teamId: string
  teamName: string
  challengeId: string
  challengeName: string
  status: 'healthy' | 'down' | 'unknown'
}

export interface AwdDashboardDto {
  competitionId: string
  currentRound: number
  roundDurationSeconds: number
  remainingSeconds: number
  services: AwdServiceStatusDto[]
}

export interface AwdTeamDto {
  id: string
  name: string
}

export interface AwdChallengeDto {
  id: string
  title: string
}

export interface AwdPatchSubmissionDto {
  id?: string
  submissionId?: string
  challengeId: string
  challengeName?: string
  challengeTitle?: string
  status: 'Pending' | 'Running' | 'Retrying' | 'Applied' | 'Verified' | 'Rejected' | 'Failed'
  submittedAt?: string
  createdAt?: string
  lastError?: string
  validationLog?: string
}

export type AwdAwarenessEventType
  = | 'attack'
    | 'service_down'
    | 'service_recovered'
    | 'round_start'
    | 'flag_refresh'
    | 'checker_error'
    | 'gamebox_restart'

export type AwdAwarenessEventResult
  = | 'success'
    | 'failed'
    | 'error'
    | 'recovered'
    | 'system'

export interface AwdAwarenessEventDto {
  id?: string
  type: AwdAwarenessEventType
  result?: AwdAwarenessEventResult
  attackerTeamId?: string
  attackerTeamName?: string
  victimTeamId?: string
  victimTeamName?: string
  teamId?: string
  teamName?: string
  challengeId?: string
  challengeName?: string
  serviceName?: string
  round?: number
  timestamp?: string
  message?: string
  reason?: string
}

export type AwdFlagSubmitOutcome
  = | { kind: 'correct' }
    | { kind: 'incorrect', message?: string }
    | { kind: 'failed' }
    | { kind: 'skipped' }

export type AwdPatchSubmitOutcome
  = | { kind: 'ok', submissionId?: string }
    | { kind: 'failed' }
    | { kind: 'skipped' }

export interface AwdAttackFailedFallbacks {
  currentTeamName?: string
  selectedTargetName?: string
  selectedServiceName?: string
}

// Canonical behavior follows V1's AwdDashboardWorkspace: 10s dashboard polling,
// SignalR game hub feeding the attack log (capped at 100), and local awareness
// events for failed attacks (capped at 50). Themes supply their own feedback
// copy via outcome objects and fallback strings.
export function useAwdDashboardPage(gameModeType: () => string) {
  const route = useRoute()
  const queryClient = useQueryClient()
  const auth = useAuthStore()
  const scoreStore = useScoreStore()

  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
  const hasValidCompetitionId = computed(() => competitionId.value.length > 0)
  const isAwdp = computed(() => gameModeType()?.toLowerCase() === 'awdp')

  const dashboardQuery = useQuery({
    queryKey: computed(() => queryKeys.awdDashboard(competitionId.value)),
    queryFn: () => competitionApi.awdDashboard<AwdDashboardDto>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
    refetchInterval: 10_000,
  })

  const teamsQuery = useQuery({
    queryKey: computed(() => queryKeys.teams(competitionId.value)),
    queryFn: () => competitionApi.teams<AwdTeamDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const challengesQuery = useQuery({
    queryKey: computed(() => queryKeys.challenges(competitionId.value)),
    queryFn: () => competitionApi.challenges<AwdChallengeDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const patchSubmissionsQuery = useQuery({
    queryKey: computed(() => queryKeys.patchSubmissions(competitionId.value)),
    queryFn: () => competitionApi.patchSubmissions<AwdPatchSubmissionDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value) && isAwdp.value),
    refetchInterval: computed(() => isAwdp.value ? 10_000 : false),
  })

  const round = computed(() => dashboardQuery.data.value?.currentRound ?? 0)
  const remainingSeconds = computed(() => dashboardQuery.data.value?.remainingSeconds ?? 0)
  const totalSeconds = computed(() => dashboardQuery.data.value?.roundDurationSeconds ?? 300)
  const services = computed<AwdServiceStatusDto[]>(() => dashboardQuery.data.value?.services ?? [])
  const teams = teamsQuery.data
  const challenges = challengesQuery.data

  const attackLogs = ref<AttackLogDto[]>([])
  const localAwarenessEvents = ref<AwdAwarenessEventDto[]>([])

  const signalR = useSignalR({
    hubUrl: `/hubs/game?competitionId=${competitionId.value}`,
    accessToken: () => auth.accessToken,
  })

  signalR.onRoundStarted(() => {
    void dashboardQuery.refetch()
  })

  signalR.onAttackLog((log) => {
    attackLogs.value.unshift(log)
    if (attackLogs.value.length > 100)
      attackLogs.value.splice(100)
  })

  onMounted(() => {
    if (competitionId.value)
      void signalR.start()
  })

  const patchChallenge = ref('')
  const patchFile = ref<File | null>(null)
  const patchLoading = ref(false)
  const isDragOver = ref(false)
  const patchStatuses = computed(() => patchSubmissionsQuery.data.value ?? [])

  async function submitPatch(): Promise<AwdPatchSubmitOutcome> {
    if (!patchFile.value || !patchChallenge.value)
      return { kind: 'skipped' }
    patchLoading.value = true
    try {
      const data = await competitionApi.submitPatch<{ submissionId?: string }>(
        competitionId.value,
        scoreStore.teamId ?? '',
        patchChallenge.value,
        patchFile.value,
      )
      await queryClient.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId.value) })
      patchFile.value = null
      return { kind: 'ok', submissionId: data?.submissionId }
    }
    catch {
      return { kind: 'failed' }
    }
    finally {
      patchLoading.value = false
    }
  }

  const selectedVictim = ref('')
  const selectedChallenge = ref('')
  const flagInput = ref('')
  const flagLoading = ref(false)

  async function submitFlag(): Promise<AwdFlagSubmitOutcome> {
    if (!flagInput.value.trim() || !selectedChallenge.value)
      return { kind: 'skipped' }
    flagLoading.value = true
    try {
      const data = await competitionApi.submitFlag<{ correct?: boolean, message?: string }>(
        competitionId.value,
        scoreStore.teamId ?? '',
        selectedChallenge.value,
        flagInput.value.trim(),
      )
      if (data?.correct) {
        flagInput.value = ''
        return { kind: 'correct' }
      }
      return { kind: 'incorrect', message: data?.message }
    }
    catch {
      return { kind: 'failed' }
    }
    finally {
      flagLoading.value = false
    }
  }

  function enqueueAttackFailed(reason: string, fallbacks: AwdAttackFailedFallbacks = {}) {
    const timestamp = new Date().toISOString()
    const victim = teams.value?.find(team => team.id === selectedVictim.value)
    const challenge = challenges.value?.find(item => item.id === selectedChallenge.value)

    localAwarenessEvents.value.unshift({
      id: `local-attack-failed:${competitionId.value}:${selectedVictim.value || 'unknown'}:${selectedChallenge.value}:${timestamp}`,
      type: 'attack',
      result: 'failed',
      attackerTeamId: scoreStore.teamId ?? undefined,
      attackerTeamName: scoreStore.myTeamName ?? auth.user?.userName ?? fallbacks.currentTeamName,
      victimTeamId: victim?.id ?? selectedVictim.value,
      victimTeamName: victim?.name ?? fallbacks.selectedTargetName,
      challengeId: challenge?.id ?? selectedChallenge.value,
      challengeName: challenge?.title ?? fallbacks.selectedServiceName,
      round: round.value,
      timestamp,
      reason,
    })

    if (localAwarenessEvents.value.length > 50)
      localAwarenessEvents.value.splice(50)
  }

  return {
    competitionId,
    hasValidCompetitionId,
    isAwdp,
    round,
    remainingSeconds,
    totalSeconds,
    services,
    teams,
    challenges,
    loadingDashboard: dashboardQuery.isLoading,
    dashboardError: dashboardQuery.isError,
    dashboardQueryError: dashboardQuery.error,
    refetchDashboard: dashboardQuery.refetch,
    refetchChallenges: challengesQuery.refetch,
    attackLogs,
    localAwarenessEvents,
    signalR,
    patchChallenge,
    patchFile,
    patchLoading,
    isDragOver,
    patchStatuses,
    submitPatch,
    selectedVictim,
    selectedChallenge,
    flagInput,
    flagLoading,
    submitFlag,
    enqueueAttackFailed,
  }
}
