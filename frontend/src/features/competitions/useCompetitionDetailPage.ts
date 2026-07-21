import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { challengeDirections, normalizeDirection } from '@/lib/challengeDirections'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

export interface CompetitionDetailDto {
  id: string
  title: string
  description?: string | null
  status: string
  startTime: string
  endTime: string
  gameModeType: string
  maxTeamMembers: number
  teamRegistrationAutoApprove: boolean
  tracksEnabled: boolean
  trackNames: string[]
}

export interface FirstBloodDto {
  rank: number
  teamName: string
  solvedAt?: string
}

export interface CompetitionChallengeDto {
  id: string
  title: string
  typeId: string
  points: number
  solveCount: number
  deploymentType?: string | number | null
  description?: string | null
  descriptionFormat?: string | null
  hints?: string[]
  attachmentUrl?: string | null
  patchTemplateUrl?: string | null
  firstBloods?: FirstBloodDto[]
}

export interface SubmissionsResponseDto {
  competitionId: string
  teamId: string
  solvedChallenges: { challengeId: string }[]
}

export interface PatchSubmissionStatusDto {
  id?: string
  challengeId: string
  status: string | number
  fixStatus?: string | number
  attemptNumber?: number
  fileName?: string
  fixEntry?: string
  submittedAt?: string
  validatedAt?: string | null
  validationDetail?: string | null
}

export interface CompetitionViewResultDto<T> {
  viewKey: string
  data: T
}

export interface AwdpRoundStateDto {
  roundNumber: number
  status: string
  startTime: string
  endTime?: string | null
}

export interface AwdpChallengeStateDto {
  challengeId: string
  instanceStatus: string
  breakStatus: string
  fixStatus: string
  serviceStatus: string
  currentRoundAttackScore: number
  currentRoundDefenseScore: number
  attackScorePerRound: number
  defenseScorePerRound: number
  attackAttempts: number
  defenseAttempts: number
  maxAttackAttempts: number
  maxDefenseAttempts: number
  remainingAttackAttempts: number
  remainingDefenseAttempts: number
  canSubmitFlag: boolean
  canRequestDefense: boolean
  allowAttackAfterBreakSuccess: boolean
  allowDefenseAfterFixSuccess: boolean
  fixEntry: string
  lastValidationDetail?: string | null
  cooldownUntil?: string | null
}

export interface AwdpStateDataDto {
  code: string
  competitionId: string
  teamId?: string
  currentRound?: AwdpRoundStateDto | null
  serverTime: string
  challenges: AwdpChallengeStateDto[]
}

export interface CompetitionTeamDto {
  id: string
  name: string
  inviteToken: string
  memberCount: number
  isLocked: boolean
  isBanned: boolean
  bannedReason?: string | null
  trackName?: string | null
  registrationStatus: string
  isCaptain: boolean
}

export interface LeaderboardEntryDto {
  teamId?: string | null
  teamName?: string | null
  totalScore?: number | null
  score?: number | null
}

export interface LeaderboardResponseDto {
  entries?: LeaderboardEntryDto[]
}

// Canonical behavior follows V1's CompetitionDetailWorkspace: every query is
// gated only on the route id (AWDP queries additionally on mode/team), the
// header score syncs into the score store, and solving a challenge refetches
// submissions plus invalidates the AWDP state.
export function useCompetitionDetailPage() {
  const route = useRoute()
  const router = useRouter()
  const queryClient = useQueryClient()
  const auth = useAuthStore()
  const scoreStore = useScoreStore()

  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id.trim() : '')
  const hasValidCompetitionId = computed(() => competitionId.value.length > 0)

  const competitionQuery = useQuery({
    queryKey: computed(() => queryKeys.competition(competitionId.value)),
    queryFn: () => competitionApi.get<CompetitionDetailDto>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const challengesQuery = useQuery({
    queryKey: computed(() => queryKeys.challenges(competitionId.value)),
    queryFn: () => competitionApi.challenges<CompetitionChallengeDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const submissionsQuery = useQuery({
    queryKey: computed(() => queryKeys.submissions(competitionId.value)),
    queryFn: () => competitionApi.submissions<SubmissionsResponseDto>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const myTeamsQuery = useQuery({
    queryKey: computed(() => queryKeys.myCompetitionTeams(competitionId.value)),
    queryFn: () => competitionApi.myTeams<CompetitionTeamDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value)),
  })

  const competition = competitionQuery.data
  const isAwdMode = computed(() => (competition.value?.gameModeType ?? '').toLowerCase() === 'awd')
  const isAwdpMode = computed(() => (competition.value?.gameModeType ?? '').toLowerCase() === 'awdp')
  const canManageCompetition = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))
  const approvedTeam = computed(() =>
    (myTeamsQuery.data.value ?? []).find(team => team.registrationStatus === 'approved') ?? null)
  const currentTeam = computed(() => approvedTeam.value ?? myTeamsQuery.data.value?.[0] ?? null)
  const canUseParticipantActions = computed(() => Boolean(approvedTeam.value && !approvedTeam.value.isBanned))
  const canAccessChallenges = computed(() =>
    canManageCompetition.value || Boolean(approvedTeam.value && !approvedTeam.value.isBanned))

  const headerLeaderboardQuery = useQuery({
    queryKey: computed(() => [...queryKeys.leaderboard(competitionId.value), 'header-score']),
    queryFn: () => competitionApi.leaderboard(competitionId.value) as Promise<LeaderboardResponseDto>,
    enabled: computed(() => Boolean(competitionId.value) && Boolean(approvedTeam.value?.id)),
    refetchInterval: computed(() => approvedTeam.value?.id ? 10_000 : false),
  })

  watch(
    () => [
      competitionId.value,
      approvedTeam.value?.id,
      currentTeam.value?.id,
      headerLeaderboardQuery.data.value?.entries,
    ],
    () => {
      if (!competitionId.value) {
        scoreStore.reset()
        return
      }

      if (!approvedTeam.value) {
        scoreStore.setCurrentTeamScore(
          competitionId.value,
          currentTeam.value?.id ?? null,
          currentTeam.value?.name ?? auth.user?.userName ?? null,
          null,
        )
        return
      }

      const entries = headerLeaderboardQuery.data.value?.entries ?? []
      const currentEntry = entries.find(entry => entry.teamId === approvedTeam.value?.id)
      if (!currentEntry) {
        scoreStore.setCurrentTeamScore(competitionId.value, approvedTeam.value.id, approvedTeam.value.name, 0)
        return
      }

      scoreStore.updateFromLeaderboard(entries as never, approvedTeam.value.id, competitionId.value)
    },
    { immediate: true },
  )

  const patchSubmissionsQuery = useQuery({
    queryKey: computed(() => queryKeys.patchSubmissions(competitionId.value)),
    queryFn: () => competitionApi.patchSubmissions<PatchSubmissionStatusDto[]>(competitionId.value),
    enabled: computed(() => Boolean(competitionId.value) && isAwdpMode.value),
    refetchInterval: computed(() => isAwdpMode.value ? 10_000 : false),
  })

  const awdpStateQuery = useQuery({
    queryKey: computed(() => queryKeys.awdpState(competitionId.value)),
    queryFn: () => competitionApi.view<CompetitionViewResultDto<AwdpStateDataDto>>(competitionId.value, 'challenge-state'),
    enabled: computed(() => Boolean(competitionId.value) && isAwdpMode.value && Boolean(approvedTeam.value?.id)),
    refetchInterval: computed(() => isAwdpMode.value ? 5_000 : false),
  })

  const awdpStateData = computed(() => awdpStateQuery.data.value?.data ?? null)

  const myTeamScore = computed(() => scoreStore.myTeamScore)

  const solvedIds = computed(() =>
    new Set((submissionsQuery.data.value?.solvedChallenges ?? []).map(s => s.challengeId)))
  const effectiveChallenges = computed<CompetitionChallengeDto[]>(() => challengesQuery.data.value ?? [])
  const isLoading = computed(() => competitionQuery.isLoading.value || challengesQuery.isLoading.value)

  const activeDirection = ref('ALL')
  const hideSolved = ref(false)

  const filteredChallenges = computed(() => {
    return effectiveChallenges.value.filter((challenge) => {
      const solved = solvedIds.value.has(challenge.id)
      const matchesDirection = activeDirection.value === 'ALL' || normalizeDirection(challenge.typeId) === activeDirection.value
      const matchesSolved = !hideSolved.value || !solved
      return matchesDirection && matchesSolved
    })
  })

  const directionOptions = computed(() => {
    const counts = new Map<string, { total: number, unsolved: number }>()
    for (const dir of challengeDirections)
      counts.set(dir, { total: 0, unsolved: 0 })

    for (const challenge of effectiveChallenges.value) {
      const direction = normalizeDirection(challenge.typeId)
      const current = counts.get(direction) ?? { total: 0, unsolved: 0 }
      current.total += 1
      if (!solvedIds.value.has(challenge.id))
        current.unsolved += 1
      counts.set(direction, current)
    }

    const entries = challengeDirections
      .filter(dir => (counts.get(dir)?.total ?? 0) > 0)
      .map(dir => ({ direction: dir, ...(counts.get(dir) ?? { total: 0, unsolved: 0 }) }))
      .sort((a, b) => a.direction.localeCompare(b.direction))

    return [
      {
        direction: 'ALL',
        total: effectiveChallenges.value.length,
        unsolved: effectiveChallenges.value.filter(challenge => !solvedIds.value.has(challenge.id)).length,
      },
      ...entries,
    ]
  })

  const selectedChallenge = ref<CompetitionChallengeDto | null>(null)
  const instanceChallengeIds = ref(new Set<string>())
  const defenseChallengeIds = ref(new Set<string>())

  function selectChallenge(challenge: CompetitionChallengeDto) {
    selectedChallenge.value = challenge
  }

  function markInstanceCreated(challengeId: string) {
    instanceChallengeIds.value = new Set(instanceChallengeIds.value).add(challengeId)
  }

  function markDefenseRequested(challengeId: string) {
    defenseChallengeIds.value = new Set(defenseChallengeIds.value).add(challengeId)
  }

  const selectedChallengePatchSubmissions = computed(() => {
    if (!selectedChallenge.value)
      return []
    return (patchSubmissionsQuery.data.value ?? []).filter(item => item.challengeId === selectedChallenge.value?.id)
  })

  const selectedChallengeAwdpState = computed(() => {
    if (!selectedChallenge.value)
      return null
    return awdpStateData.value?.challenges.find(item => item.challengeId === selectedChallenge.value?.id) ?? null
  })

  const canSubmitSelectedChallenge = computed(() =>
    canUseParticipantActions.value && (!isAwdpMode.value || selectedChallengeAwdpState.value?.canSubmitFlag !== false))
  const canRequestSelectedDefense = computed(() =>
    canUseParticipantActions.value && (!isAwdpMode.value || selectedChallengeAwdpState.value?.canRequestDefense !== false))

  function onChallengeSolved() {
    void submissionsQuery.refetch()
    void queryClient.invalidateQueries({ queryKey: queryKeys.submissions(competitionId.value) })
    void queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
  }

  function handleInstanceCreated(challengeId: string) {
    markInstanceCreated(challengeId)
    void queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
  }

  function handlePatchUploaded() {
    void queryClient.invalidateQueries({ queryKey: queryKeys.patchSubmissions(competitionId.value) })
    void queryClient.invalidateQueries({ queryKey: queryKeys.awdpState(competitionId.value) })
  }

  function goCompetitions() {
    void router.push({ name: 'competitions' })
  }

  function goRegistration() {
    void router.push({ name: 'competition-register', params: { id: competitionId.value } })
  }

  function goAwdpScreen() {
    void router.push({ name: 'awdp-screen', params: { gameId: competitionId.value } })
  }

  function goAwdDashboard() {
    void router.push({ name: 'awd-dashboard', params: { id: competitionId.value } })
  }

  function goKohDashboard() {
    void router.push({ name: 'koh-dashboard', params: { id: competitionId.value } })
  }

  function goPenetrationDashboard() {
    void router.push({ name: 'penetration-dashboard', params: { id: competitionId.value } })
  }

  return {
    competitionId,
    hasValidCompetitionId,
    competition,
    isLoading,
    loadingCompetition: competitionQuery.isLoading,
    loadingMyTeams: myTeamsQuery.isLoading,
    loadingChallenges: challengesQuery.isLoading,
    challengesError: challengesQuery.isError,
    competitionError: competitionQuery.isError,
    competitionQueryError: competitionQuery.error,
    refetchCompetition: competitionQuery.refetch,
    isAwdMode,
    isAwdpMode,
    canManageCompetition,
    approvedTeam,
    currentTeam,
    canUseParticipantActions,
    canAccessChallenges,
    awdpStateData,
    myTeamScore,
    awdpStateLoading: awdpStateQuery.isLoading,
    awdpStateError: awdpStateQuery.isError,
    solvedIds,
    effectiveChallenges,
    filteredChallenges,
    directionOptions,
    activeDirection,
    hideSolved,
    selectedChallenge,
    selectChallenge,
    instanceChallengeIds,
    defenseChallengeIds,
    markInstanceCreated,
    markDefenseRequested,
    selectedChallengePatchSubmissions,
    selectedChallengeAwdpState,
    canSubmitSelectedChallenge,
    canRequestSelectedDefense,
    onChallengeSolved,
    handleInstanceCreated,
    handlePatchUploaded,
    goCompetitions,
    goRegistration,
    goAwdpScreen,
    goAwdDashboard,
    goKohDashboard,
    goPenetrationDashboard,
  }
}
