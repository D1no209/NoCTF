import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { competitionApi } from '@/api/noctf'
import { useSignalR } from '@/composables/useSignalR'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

export interface ScoreboardEntryDto {
  rank?: number
  teamId?: string
  teamName?: string
  trackName?: string | null
  totalScore?: number
  score?: number
  solvedCount?: number
  firstSolveAt?: string | null
}

export interface ScoreboardTrendPointDto {
  timestamp: string
  score: number
}

export interface ScoreboardTrendSeriesDto {
  teamId: string
  teamName: string
  points: ScoreboardTrendPointDto[]
}

export interface ScoreboardTrendResponseDto {
  series?: ScoreboardTrendSeriesDto[]
}

export interface ScoreboardDirectionScoreDto {
  direction: string
  score: number
  solvedCount: number
}

export interface ScoreboardMemberSolveDto {
  challengeId: string
  challengeTitle: string
  direction: string
  submittedAt: string
}

export interface ScoreboardMemberHistoryDto {
  userId: string
  userName: string
  solves: ScoreboardMemberSolveDto[]
}

export interface ScoreboardChallengeScoreDto {
  challengeId: string
  challengeTitle: string
  direction: string
  currentPoints: number
  baseScore: number
  bonusScore: number
  totalScore: number
  bloodRank?: number | null
  solvedAt?: string | null
}

export interface ScoreboardTeamDetailDto {
  teamId: string
  teamName: string
  trackName?: string | null
  totalScore: number
  solvedCount: number
  directionScores: ScoreboardDirectionScoreDto[]
  challengeScores: ScoreboardChallengeScoreDto[]
  members: ScoreboardMemberHistoryDto[]
}

export interface UseScoreboardOptions {
  competitionId: () => string
  teamId: () => string | null | undefined
  full?: () => boolean | undefined
  onScoreUpdate?: (entries: ScoreboardEntryDto[]) => void
}

// Canonical behavior follows V1's ScoreboardView: an initial fetch, SignalR
// leaderboard hub driving live snapshots, and a 10s polling fallback whenever
// the socket is down. Scores mirror into the score store for the chrome.
export function useScoreboard(options: UseScoreboardOptions) {
  const auth = useAuthStore()
  const scoreStore = useScoreStore()

  const entries = ref<ScoreboardEntryDto[]>([])
  const trend = ref<ScoreboardTrendSeriesDto[]>([])
  const loading = ref(true)
  const usingFallback = ref(false)
  const selectedTeamId = ref<string | null>(null)
  const selectedTeam = ref<ScoreboardTeamDetailDto | null>(null)
  const detailLoading = ref(false)
  let pollInterval: ReturnType<typeof setInterval> | null = null

  const sortedEntries = computed(() => entries.value.map(entry => ({
    ...entry,
    totalScore: entry.totalScore ?? entry.score ?? 0,
    solvedCount: entry.solvedCount ?? 0,
  })))

  const displayedEntries = computed(() => {
    if (options.full?.() === false)
      return sortedEntries.value.slice(0, 10)
    return sortedEntries.value
  })

  function applyLeaderboard(nextEntries: ScoreboardEntryDto[]) {
    entries.value = nextEntries
    scoreStore.updateFromLeaderboard(entries.value as never, options.teamId(), options.competitionId())
    options.onScoreUpdate?.(entries.value)
  }

  async function fetchLeaderboard() {
    try {
      const [leaderboardData, trendData] = await Promise.all([
        competitionApi.leaderboard(options.competitionId()) as Promise<{ entries?: ScoreboardEntryDto[] }>,
        competitionApi.leaderboardTrend<ScoreboardTrendResponseDto>(options.competitionId()),
      ])
      applyLeaderboard(leaderboardData?.entries ?? [])
      trend.value = trendData?.series ?? []
    }
    catch {
      // Keep the last successful snapshot while polling.
    }
    finally {
      loading.value = false
    }
  }

  async function openTeamDetail(teamId?: string) {
    if (!teamId)
      return
    selectedTeamId.value = teamId
    detailLoading.value = true
    selectedTeam.value = null
    try {
      selectedTeam.value = await competitionApi.leaderboardTeam<ScoreboardTeamDetailDto>(options.competitionId(), teamId)
    }
    finally {
      detailLoading.value = false
    }
  }

  function startPolling() {
    if (pollInterval)
      return
    usingFallback.value = true
    pollInterval = setInterval(fetchLeaderboard, 10_000)
  }

  function stopPolling() {
    if (pollInterval) {
      clearInterval(pollInterval)
      pollInterval = null
    }
    usingFallback.value = false
  }

  const { connection, isConnected, start } = useSignalR({
    hubUrl: '/hubs/competition',
    accessToken: () => auth.accessToken,
    onConnected: () => {
      connection.value?.invoke('JoinCompetition', options.competitionId()).catch(() => undefined)
      stopPolling()
    },
    onDisconnected: () => {
      startPolling()
    },
    onReconnected: () => {
      stopPolling()
      void fetchLeaderboard()
    },
  })

  watch(connection, (conn) => {
    if (!conn)
      return
    conn.on('leaderboardRefreshed', (notification: { competitionId?: string }) => {
      if (notification?.competitionId && notification.competitionId !== options.competitionId())
        return
      void fetchLeaderboard()
    })
    conn.on('competitionLifecycleChanged', (notification: { competitionId?: string }) => {
      if (notification?.competitionId === options.competitionId())
        void fetchLeaderboard()
    })
    conn.on('ReceiveScoreUpdate', () => {
      void fetchLeaderboard()
    })
  }, { immediate: true })

  watch(() => options.teamId(), () => {
    scoreStore.updateFromLeaderboard(entries.value as never, options.teamId(), options.competitionId())
  })

  onMounted(async () => {
    await fetchLeaderboard()
    try {
      await start()
    }
    catch {
      startPolling()
    }
  })

  onUnmounted(() => {
    stopPolling()
  })

  return {
    entries,
    trend,
    loading,
    usingFallback,
    isConnected,
    sortedEntries,
    displayedEntries,
    selectedTeamId,
    selectedTeam,
    detailLoading,
    openTeamDetail,
    fetchLeaderboard,
  }
}
