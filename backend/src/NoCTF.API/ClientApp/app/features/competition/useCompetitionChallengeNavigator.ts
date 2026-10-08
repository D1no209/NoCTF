import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { challengeTagOptions, matchesAllTags, tagsFromQuery, uniqueTags } from '../../lib/challenge-tags'
import { toRefs } from 'vue'

import { ShieldCheck, Swords, Users } from '@lucide/vue'
import { getMyTeamEndpoint, listChallengesEndpoint } from '../../api'
import type { NoCtfapiEndpointsChallengesChallengeSummaryResponse, NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol } from '../../api'
import { bloodRankLabel } from '../leaderboard/types'
import { directionGlyph } from '../../utils/directions'
import { challengeProgressIcon } from './challenge-progress-icon'
import { scoreboardBreakdown, scoreboardColumnsForChallenge, scoreboardCurrentChallengeScore, scoreboardSlot } from '../../utils/scoreboard'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'

type Challenge = NoCtfapiEndpointsChallengesChallengeSummaryResponse
type BloodRank = 'First' | 'Second' | 'Third'
const bloodOrder: Record<BloodRank, number> = { First: 0, Second: 1, Third: 2 }

/** Null denotes an invalid expression; an empty string matches all challenge names. */
export function compileChallengeTitleSearch(search: string, regex: boolean): string | RegExp | null {
  const query = search.trim()
  if (!regex || !query) return query.toLocaleLowerCase()
  try {
    return new RegExp(query, 'i')
  } catch {
    return null
  }
}

export function isChallengeVisible(
  challenge: Pick<Challenge, 'title' | 'locked' | 'tags'>,
  filters: { hideSolved: boolean; hideLocked: boolean; solvedByMyTeam: boolean; search: string | RegExp | null; tags?: readonly string[] },
): boolean {
  return (!filters.hideSolved || !filters.solvedByMyTeam)
    && (!filters.hideLocked || !challenge.locked)
    && matchesAllTags(challenge.tags ?? [], filters.tags ?? [])
    && filters.search !== null
    && (filters.search instanceof RegExp
      ? filters.search.test(challenge.title ?? '')
      : !filters.search || (challenge.title ?? '').toLocaleLowerCase().includes(filters.search))
}

export function affectsCompetitionChallengeList(kind: string): boolean {
  return kind === 'ChallengeCreated'
    || kind === 'ChallengeUpdated'
    || kind === 'ChallengePublished'
    || kind === 'ChallengeUnpublished'
    || kind === 'ChallengeDeleted'
    || kind === 'ChallengeDescriptionUpdated'
    || kind === 'TeamRegistrationChanged'
    || kind === 'TeamTrackChanged'
    || kind === 'TeamBanned'
    || kind === 'TeamUnbanned'
    || kind === 'TeamBanCorrectionPublished'
    || kind === 'CompetitionUpdated'
    || kind === 'GameplayFactAdjudicated'
}

interface ChallengeBloodMark {
  rank: BloodRank
  teamId: string
  teamName: string | null
  earnedByMyTeam: boolean
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

  const isCtf = computed(() => ctx.competition.value?.mode === 'Ctf')

  const isAwdp = computed(() => ctx.competition.value?.mode === 'Awdp')

  const items = ref<Challenge[]>([])

  const loading = ref(true)

  const error = ref<UiMessage | null>(null)

  const dataScope = ref<NoCtfapiEndpointsCompetitionsLeaderboardDataScopeProtocol>('Live')

  const myTeamId = ref<string | null>(null)

  const hideSolved = ref(false)

  const hideLocked = ref(false)

  const hidesLockedChallenges = computed(() => isCtf.value && hideLocked.value)

  const search = ref('')
  const regexSearch = ref(false)
  const route = useRoute()
  const router = useRouter()
  const selectedTags = computed(() => tagsFromQuery(route.query.tag))
  const tagOptions = computed(() => challengeTagOptions(items.value))
  function updateSelectedTags(tags: string[]) {
    const names = uniqueTags(tags)
    const query = { ...route.query }
    if (names.length) query.tag = names
    else delete query.tag
    void router.replace({ query })
  }

  let initialized = false
  let unwatch: (() => void) | undefined

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

  async function loadChallenges(): Promise<void> {
    const { data, error: requestError, response } = await listChallengesEndpoint({
      path: { competitionId: props.competitionId },
    })
    loading.value = false
    if (requestError || !data) {
      if (response?.status === 404) items.value = []
      error.value = parseApiError(requestError, describeMessage("challenges.error.loadQuestionFailed")).displayMessage
      if (!initialized) emit('ready', null)
      return
    }
    items.value = (data.items ?? []).filter(challenge => challenge.isPublished)
    dataScope.value = data.dataScope ?? 'Live'
    error.value = null
    initialized = true
  }

  const refreshChallenges = createTrailingRefresh(loadChallenges)

  onMounted(() => {
    unwatch = watchCompetition(props.competitionId, {
      competitionEventChanged: notification => {
        if (affectsCompetitionChallengeList(notification.kind)) {
          void Promise.all([refreshChallenges(), loadMyTeam()])
        }
      },
      onReconnected: () => void refreshChallenges(),
    })
    void Promise.all([refreshChallenges(), loadMyTeam()])
  })

  onUnmounted(() => unwatch?.())

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
              earnedByMyTeam: team.teamId === myTeamId.value,
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
    if (progress.attackSucceeded && progress.defenseSucceeded) return translate("common.label.attackDefenseSucceeded")
    if (progress.attackSucceeded) return translate("common.label.attackSucceeded")
    if (progress.defenseSucceeded) return translate("common.label.defenseSucceeded")
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
    return progress.bloodRank ? bloodRankLabel(progress.bloodRank) : translate("common.label.solved.competitionChallengeNavigator")
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
    return blood.teamName ?? `${translate('common.label.teamId')}: ${blood.teamId}`
  }

  const groups = computed(() => {
    const grouped = new Map<string, Challenge[]>()
    for (const item of items.value) {
      const direction = (item.directionId ? item.direction : directionLabel(item.direction)) || translate("common.label.uncategorized")
      const challenges = grouped.get(direction) ?? []
      challenges.push(item)
      grouped.set(direction, challenges)
    }
    return [...grouped.entries()].map(([direction, challenges]) => ({
      direction,
      challenges: challenges.sort((left, right) => (left.order ?? 0) - (right.order ?? 0)),
    }))
  })

  const normalizedSearch = computed(() => search.value.trim())
  const titleSearch = computed(() => compileChallengeTitleSearch(search.value, regexSearch.value))
  const searchError = computed<UiMessage | null>(() => titleSearch.value === null
    ? describeMessage('challengeNavigator.invalidRegex') : null)

  const visibleGroups = computed(() => groups.value
    .map(group => ({
      ...group,
      challenges: group.challenges.filter(challenge => isChallengeVisible(challenge, {
        hideSolved: hideSolved.value,
        hideLocked: hidesLockedChallenges.value,
        solvedByMyTeam: hideSolved.value && !!progressFor(challenge.id)?.solvedByMyTeam,
        search: titleSearch.value,
        tags: selectedTags.value,
      })),
    }))
    .filter(group => group.challenges.length > 0))

  const emptyLabel = computed(() => normalizedSearch.value || selectedTags.value.length || (hideSolved.value && hidesLockedChallenges.value)
    ? translate('challengeNavigator.noMatches')
    : hidesLockedChallenges.value ? translate('challengeNavigator.noUnlockedChallenges')
      : hideSolved.value ? translate('challenges.label.unsolvedChallenges') : translate('challenges.competitionChallenge.description.therePublishedTopicsYet'))

  const groupOptions = computed(() => visibleGroups.value.map(group => ({
    value: group.direction,
    label: group.direction,
    icon: group.challenges[0]?.directionIcon,
    items: group.challenges.filter(challenge => Boolean(challenge.id))
      .map(challenge => ({ value: challenge.id!, label: challenge.title ?? '', challenge })),
  })))
  const listOptions = computed(() => groupOptions.value.flatMap(group => group.items))
  const visibleChallengeIds = computed(() => listOptions.value
    .filter(item => !item.challenge.locked).map(item => item.value))

  watch([visibleChallengeIds, () => props.selectedChallengeId], ([ids, selectedId]) => {
    if (!loading.value && ids.length && !ids.includes(selectedId ?? '')) emit('ready', ids[0]!)
  }, { flush: 'post' })

  function selectChallenge(challengeId: string) {
    if (items.value.find(item => item.id === challengeId)?.locked) return
    emit('select', challengeId)
  }

  function groupIcon(name: string) { return items.value.find(item => (item.directionId ? item.direction : directionLabel(item.direction)) === name)?.directionIcon }

  return {
      groupIcon, directionGlyph,
      ...toRefs(props),
      ShieldCheck,
      Swords,
      Users,
      bloodRankLabel,
      emit,
      isCtf,
      isAwdp,
      items,
      loading,
      error,
      dataScope,
      hideSolved,
      hideLocked,
      search,
      regexSearch,
      searchError,
      selectedTags, tagOptions, updateSelectedTags,
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
