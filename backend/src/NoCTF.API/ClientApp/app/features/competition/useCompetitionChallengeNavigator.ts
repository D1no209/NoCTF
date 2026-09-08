import { toRefs } from 'vue'

import { ChevronRight, Flag, ShieldCheck, Swords, Users } from '@lucide/vue'
import { getMyTeamEndpoint, listChallengesEndpoint } from '../../api'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol } from '../../api'
import { bloodRankLabel } from '../leaderboard/types'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardCurrentChallengeScore, scoreboardSlot } from '../../utils/scoreboard'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse

type Events = {
  ready: [challengeId: string | null]
  select: [challengeId: string]
}

interface ChallengeProgress {
  solveCount: number
  solvedByMyTeam: boolean
  bloodRank: string | null
  attackCount: number
  defenseCount: number
  attackSucceeded: boolean
  defenseSucceeded: boolean
}

/** Owns state, effects and commands for CompetitionChallengeNavigator. */
export function useCompetitionChallengeNavigator(props: Readonly<{
  competitionId: string
  selectedChallengeId?: string | null
}>,
emit: { (event: "ready", ...args: [challengeId: string | null]): void; (event: "select", ...args: [challengeId: string]): void }) {
  const ctx = inject(competitionContextKey)!

  const { isLoggedIn } = useAuth()

  const isAwdp = computed(() => ctx.competition.value?.mode === 'Awdp')

  const items = ref<Challenge[]>([])

  const loading = ref(true)

  const error = ref<string | null>(null)

  const dataScope = ref<NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol>('Live')

  const myTeamId = ref<string | null>(null)

  const hideSolved = ref(false)

  const collapsedDirections = ref<Set<string>>(new Set())

  const board = useScoreboardMatrix(props.competitionId)

  async function loadMyTeam(): Promise<void> {
    if (!isLoggedIn.value) {
      myTeamId.value = null
      return
    }
    const { data, error: requestError } = await getMyTeamEndpoint({
      path: { competitionId: props.competitionId },
    })
    myTeamId.value = requestError ? null : data?.id ?? null
  }

  onMounted(async () => {
    const [{ data, error: requestError }] = await Promise.all([
      listChallengesEndpoint({ path: { competitionId: props.competitionId } }),
      loadMyTeam(),
    ])
    loading.value = false
    if (requestError || !data) {
      error.value = parseApiError(requestError, translate("ui.failedToLoadQuestion")).message
      emit('ready', null)
      return
    }
    items.value = (data.items ?? []).filter(challenge => challenge.isPublished)
    dataScope.value = data.dataScope ?? 'Live'
    const selectedExists = items.value.some(item => item.id === props.selectedChallengeId)
    emit('ready', selectedExists ? props.selectedChallengeId ?? null : items.value[0]?.id ?? null)
  })

  watch(isLoggedIn, () => void loadMyTeam())

  watch(
    () => props.selectedChallengeId,
    (selectedChallengeId) => {
      if (loading.value || !items.value.length) return
      if (!selectedChallengeId || !items.value.some(item => item.id === selectedChallengeId))
        emit('ready', items.value[0]?.id ?? null)
    },
  )

  const progressByChallenge = computed(() => {
    const progress = new Map<string, ChallengeProgress>()
    if (board.snapshot.value?.dataScope === 'Hidden') return progress
  
    for (const challenge of board.catalog.value?.items ?? []) {
      if (!challenge.id) continue
      const columns = scoreboardColumnsForChallenge(board.schema.value, challenge.id)
      const current: ChallengeProgress = {
        solveCount: 0,
        solvedByMyTeam: false,
        bloodRank: null,
        attackCount: 0,
        defenseCount: 0,
        attackSucceeded: false,
        defenseSucceeded: false,
      }
      for (const team of board.snapshot.value?.teams ?? []) {
        let teamSolved = false
        let teamAttack = false
        let teamDefense = false
        for (const column of columns) {
          if (column.index === undefined) continue
          const slot = scoreboardSlot(team, column.index)
          if (!slot) continue
          teamSolved ||= (scoreboardBreakdown(slot, 'Solve')?.successfulCount ?? 0) > 0
          teamAttack ||= (scoreboardBreakdown(slot, 'Attack')?.successfulCount ?? 0) > 0
          teamDefense ||= (scoreboardBreakdown(slot, 'Defense')?.successfulCount ?? 0) > 0
          if (team.teamId === myTeamId.value && !current.bloodRank) {
            const award = slot.entries?.find(entry => entry.award)?.award
            current.bloodRank = award === 'FirstBlood' ? 'First'
              : award === 'SecondBlood' ? 'Second'
                : award === 'ThirdBlood' ? 'Third' : null
          }
        }
        if (teamSolved) current.solveCount += 1
        if (teamAttack) current.attackCount += 1
        if (teamDefense) current.defenseCount += 1
        if (team.teamId === myTeamId.value) {
          current.attackSucceeded = teamAttack
          current.defenseSucceeded = teamDefense
          current.solvedByMyTeam = isAwdp.value ? teamAttack && teamDefense : teamSolved
        }
      }
      progress.set(challenge.id, current)
    }
    return progress
  })

  function progressFor(challengeId?: string): ChallengeProgress | null {
    if (!board.snapshot.value || board.snapshot.value.dataScope === 'Hidden') return null
    return progressByChallenge.value.get(challengeId ?? '') ?? {
      solveCount: 0,
      solvedByMyTeam: false,
      bloodRank: null,
      attackCount: 0,
      defenseCount: 0,
      attackSucceeded: false,
      defenseSucceeded: false,
    }
  }

  function awdpProgressLabel(progress: ChallengeProgress | null): string | null {
    if (!progress) return null
    if (progress.attackSucceeded && progress.defenseSucceeded) return translate("ui.solved")
    if (progress.attackSucceeded) return translate("ui.attackSucceeded")
    if (progress.defenseSucceeded) return translate("ui.defenseSucceeded")
    return null
  }

  function currentScore(challengeId?: string): number | null {
    return scoreboardCurrentChallengeScore(board.snapshot.value, challengeId)
  }

  const groups = computed(() => {
    const grouped = new Map<string, Challenge[]>()
    for (const item of items.value) {
      const direction = directionLabel(item.direction) || translate("ui.uncategorized")
      const challenges = grouped.get(direction) ?? []
      challenges.push(item)
      grouped.set(direction, challenges)
    }
    return [...grouped.entries()].map(([direction, challenges]) => ({
      direction,
      challenges: challenges.sort((left, right) => (left.order ?? 0) - (right.order ?? 0)),
    }))
  })

  const visibleGroups = computed(() => groups.value
    .map(group => ({
      ...group,
      challenges: hideSolved.value
        ? group.challenges.filter(challenge => !progressFor(challenge.id)?.solvedByMyTeam)
        : group.challenges,
    }))
    .filter(group => group.challenges.length > 0))

  const visibleChallengeIds = computed(() => visibleGroups.value
    .flatMap(group => group.challenges)
    .map(challenge => challenge.id)
    .filter((id): id is string => Boolean(id)))

  watch(
    [hideSolved, visibleChallengeIds],
    ([hidden, challengeIds]) => {
      if (!hidden || !challengeIds.length || !props.selectedChallengeId) return
      if (!challengeIds.includes(props.selectedChallengeId)) emit('ready', challengeIds[0]!)
    },
    { flush: 'post' },
  )

  function isDirectionCollapsed(direction: string): boolean {
    return collapsedDirections.value.has(direction)
  }

  function toggleDirection(direction: string): void {
    const next = new Set(collapsedDirections.value)
    if (next.has(direction)) next.delete(direction)
    else next.add(direction)
    collapsedDirections.value = next
  }

  return {
      ...toRefs(props),
      ChevronRight,
      Flag,
      ShieldCheck,
      Swords,
      Users,
      bloodRankLabel,
      emit,
      isAwdp,
      items,
      loading,
      error,
      dataScope,
      hideSolved,
      board,
      progressFor,
      awdpProgressLabel,
      currentScore,
      visibleGroups,
      isDirectionCollapsed,
      toggleDirection
    }
}

export type CompetitionChallengeNavigatorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionChallengeNavigator>>>
