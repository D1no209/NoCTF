import { toRefs } from 'vue'

import { ShieldCheck, Swords, Users } from '@lucide/vue'
import { getMyTeamEndpoint, listChallengesEndpoint } from '../../api'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol } from '../../api'
import { bloodRankLabel } from '../leaderboard/types'
import { directionGlyph } from '../../utils/directions'
import { challengeProgressIcon } from './challenge-progress-icon'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardCurrentChallengeScore, scoreboardSlot } from '../../utils/scoreboard'

type Challenge = NoCtfapiEndpointsChallengesChallengeResponse
type BloodRank = 'First' | 'Second' | 'Third'
const bloodOrder: Record<BloodRank, number> = { First: 0, Second: 1, Third: 2 }

interface ChallengeBloodMark {
  rank: BloodRank
  teamId: string
  teamName: string | null
}

interface ChallengeProgress {
  solveCount: number
  solvedByMyTeam: boolean
  bloodRank: BloodRank | null
  bloods: ChallengeBloodMark[]
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

  const search = ref('')



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
  })

  watch(isLoggedIn, () => void loadMyTeam())


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
        bloods: [],
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
          const award = slot.entries?.find(entry => entry.award)?.award
          const bloodRank: BloodRank | null = award === 'FirstBlood' ? 'First'
            : award === 'SecondBlood' ? 'Second'
              : award === 'ThirdBlood' ? 'Third' : null
          if (bloodRank && team.teamId && !current.bloods.some(blood => blood.rank === bloodRank)) {
            current.bloods.push({
              rank: bloodRank,
              teamId: team.teamId,
              teamName: team.teamName?.trim() || null,
            })
          }
          if (team.teamId === myTeamId.value && !current.bloodRank) {
            current.bloodRank = bloodRank
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
      current.bloods.sort((left, right) => bloodOrder[left.rank] - bloodOrder[right.rank])
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
      bloods: [],
      attackCount: 0,
      defenseCount: 0,
      attackSucceeded: false,
      defenseSucceeded: false,
    }
  }

  function awdpProgressLabel(progress: ChallengeProgress | null): string | null {
    if (!progress) return null
    if (progress.attackSucceeded && progress.defenseSucceeded) return translate("ui.attackAndDefenseSucceeded")
    if (progress.attackSucceeded) return translate("ui.attackSucceeded")
    if (progress.defenseSucceeded) return translate("ui.defenseSucceeded")
    return null
  }

  function progressIcon(challengeId?: string) {
    return challengeProgressIcon(progressFor(challengeId), isAwdp.value)
  }

  function progressIconLabel(challengeId?: string): string | null {
    const progress = progressFor(challengeId)
    if (!progress) return null
    if (isAwdp.value) return awdpProgressLabel(progress)
    if (!progress.solvedByMyTeam) return null
    return progress.bloodRank ? bloodRankLabel(progress.bloodRank) : translate("ui.solved")
  }

  function currentScore(challengeId?: string): number | null {
    return scoreboardCurrentChallengeScore(board.snapshot.value, challengeId)
  }

  function bloodsFor(challengeId?: string): ChallengeBloodMark[] {
    return progressFor(challengeId)?.bloods ?? []
  }

  function bloodTooltip(blood: ChallengeBloodMark): string {
    return `${bloodRankLabel(blood.rank)} · ${bloodTeamName(blood)}`
  }

  function bloodTeamName(blood: ChallengeBloodMark): string {
    return blood.teamName ?? `${translate('ui.teamId')}: ${blood.teamId}`
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

  const normalizedSearch = computed(() => search.value.trim().toLocaleLowerCase())

  const visibleGroups = computed(() => groups.value
    .map(group => ({
      ...group,
      challenges: group.challenges.filter(challenge =>
        (!hideSolved.value || !progressFor(challenge.id)?.solvedByMyTeam)
        && (!normalizedSearch.value || (challenge.title ?? '').toLocaleLowerCase().includes(normalizedSearch.value))),
    }))
    .filter(group => group.challenges.length > 0))

  const emptyLabel = computed(() => normalizedSearch.value
    ? translate('challengeNavigator.noMatches')
    : hideSolved.value ? translate('ui.noUnsolvedChallenges') : translate('ui.thereAreNoPublishedTopicsYet'))

  const groupOptions = computed(() => visibleGroups.value.map(group => ({
    value: group.direction,
    label: group.direction,
    items: group.challenges.filter(challenge => Boolean(challenge.id))
      .map(challenge => ({ value: challenge.id!, label: challenge.title ?? '', challenge })),
  })))
  const listOptions = computed(() => groupOptions.value.flatMap(group => group.items))
  const visibleChallengeIds = computed(() => listOptions.value.map(item => item.value))

  watch([visibleChallengeIds, () => props.selectedChallengeId], ([ids, selectedId]) => {
    if (!loading.value && ids.length && !ids.includes(selectedId ?? '')) emit('ready', ids[0]!)
  }, { flush: 'post' })

  function selectChallenge(challengeId: string) { emit('select', challengeId) }

  return {
      directionGlyph,
      ...toRefs(props),
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
      search,
      board,
      progressFor,
      awdpProgressLabel,
      progressIcon,
      progressIconLabel,
      currentScore,
      bloodsFor,
      bloodTooltip,
      bloodTeamName,
      visibleGroups,
      emptyLabel,
      listOptions,
      groupOptions,
      selectChallenge
    }
}

export type CompetitionChallengeNavigatorViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionChallengeNavigator>>>
