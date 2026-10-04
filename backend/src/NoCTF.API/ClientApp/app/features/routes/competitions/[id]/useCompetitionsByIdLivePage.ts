import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'

import type { ComponentPublicInstance } from 'vue'
import { Clock3, Expand, Minimize, Radio, RefreshCw, ShieldCheck, Trophy, Users, X } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'
import { getCompetitionEndpoint } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../../api'
import { controlScreenChallenges, controlScreenPublicEntries, controlScreenSolveFeed, reconcileControlScreenSolves } from '../../../../utils/control-screen'
import type { ControlScreenBloodRank, ControlScreenSolve } from '../../../../utils/control-screen'
import { createTrailingRefresh } from '../../../../lib/latest-page-refresh'
import type { LiveCityBlood, LiveCityChallengeState, LiveCityScene } from '../../../../lib/live-city-3d'
import { scoreboardRankingStateLabel, scoreboardTeamSolveCount } from '../../../../utils/scoreboard'

/** Owns state, effects and commands for CompetitionsByIdLivePage. */
export function useCompetitionsByIdLivePage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const { configuration, ensureLoaded } = usePlatform()

  const { t } = useLocale()

  const board = useScoreboardMatrix(competitionId)

  const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)

  const loading = ref(true)

  const refreshing = ref(false)

  const projectionPending = ref(false)

  const error = ref<UiMessage | null>(null)

  const featuredSolve = ref<ControlScreenSolve | null>(null)

  const celebrationQueue = ref<ControlScreenSolve[]>([])

  const fullscreen = ref(false)

  const clock = ref(Date.now())

  const arenaRef = ref<HTMLElement | null>(null)

  let scene: LiveCityScene | null = null

  let disposed = false

  let clockTimer: ReturnType<typeof setInterval> | undefined

  let refreshTimer: ReturnType<typeof setInterval> | undefined

  let projectionTimer: ReturnType<typeof setTimeout> | undefined

  let celebrationTimer: ReturnType<typeof setTimeout> | undefined

  let seenSolveKeys: Set<string> | null = null

  let unwatch: (() => void) | undefined

  const entries = computed(() => controlScreenPublicEntries(board.snapshot.value))

  const rankedEntries = computed(() => entries.value)

  const challenges = computed(() => controlScreenChallenges(
    board.catalog.value,
    board.schema.value,
    entries.value,
    board.snapshot.value?.currentChallengeScores,
  ))

  const allSolves = computed(() => controlScreenSolveFeed(board.catalog.value, board.schema.value, entries.value))

  const solveFeed = computed(() => allSolves.value.slice(0, 10))

  const solvedChallengeCount = computed(() => challenges.value.filter(challenge => challenge.solveCount > 0).length)

  const totalSolveCount = computed(() => challenges.value.reduce((sum, challenge) => sum + challenge.solveCount, 0))

  const dataAsOf = computed(() => board.snapshot.value?.dataAsOf ?? board.snapshot.value?.generatedAt)

  const marqueeEnabled = computed(() => rankedEntries.value.length > 7)

  const marqueeDuration = computed(() => Math.max(14, rankedEntries.value.length * 2.2))

  const bloodToneOrder: Record<LiveCityBlood['tone'], number> = { first: 0, second: 1, third: 2 }

  const celebrationParticles = Array.from({ length: 30 }, (_, index) => ({
    id: index,
    angle: `${index * (360 / 30)}deg`,
    distance: `${10 + (index % 5) * 2.1}rem`,
    delay: `${(index % 7) * 30}ms`,
  }))

  const remainingText = computed(() => {
    const end = competition.value?.endTime ? new Date(competition.value.endTime).getTime() : null
    if (!end || competition.value?.status === 'Finished' || end <= clock.value) return t("competitions.label.competitionFinished")
    const total = Math.max(0, Math.floor((end - clock.value) / 1000))
    const days = Math.floor(total / 86400)
    const hours = Math.floor(total % 86400 / 3600)
    const minutes = Math.floor(total % 3600 / 60)
    const seconds = total % 60
    return [days ? `${days}d` : '', `${hours.toString().padStart(2, '0')}h`, `${minutes.toString().padStart(2, '0')}m`, `${seconds.toString().padStart(2, '0')}s`]
      .filter(Boolean)
      .join(' ')
  })

  const elapsedText = computed(() => {
    const start = competition.value?.startTime ? new Date(competition.value.startTime).getTime() : null
    if (!start || clock.value <= start) return '00:00:00'
    const total = Math.floor((clock.value - start) / 1000)
    const hours = Math.floor(total / 3600)
    const minutes = Math.floor(total % 3600 / 60)
    const seconds = total % 60
    return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
  })

  function bloodLabel(rank: ControlScreenBloodRank | null): string {
    if (rank === 'First') return t("common.label.firstBlood")
    if (rank === 'Second') return t("common.label.secondBlood")
    if (rank === 'Third') return t("common.label.thirdBlood")
    return t("competitions.label.solved")
  }

  function bloodClass(rank: ControlScreenBloodRank | null): string {
    if (rank === 'First') return 'live-blood-first'
    if (rank === 'Second') return 'live-blood-second'
    if (rank === 'Third') return 'live-blood-third'
    return 'live-blood-solve'
  }

  function rankClass(rank?: number): string {
    if (rank === 1) return 'live-rank-first'
    if (rank === 2) return 'live-rank-second'
    if (rank === 3) return 'live-rank-third'
    return ''
  }

  function challengeKey(value?: string | null): string {
    return (value ?? '').replace(/[^0-9a-f]/gi, '').toLowerCase()
  }

  const bloodsByChallenge = computed(() => {
    const map = new Map<string, LiveCityBlood[]>()
    for (const solve of allSolves.value) {
        if (!solve.bloodRank) continue
        const tone: LiveCityBlood['tone'] = solve.bloodRank === 'First'
          ? 'first'
          : solve.bloodRank === 'Second' ? 'second' : 'third'
        const key = challengeKey(solve.competitionChallengeId)
        const list = map.get(key) ?? []
        list.push({ label: bloodLabel(solve.bloodRank), teamName: solve.teamName, tone, points: solve.awardPoints })
        map.set(key, list)
    }
    for (const list of map.values()) list.sort((left, right) => bloodToneOrder[left.tone] - bloodToneOrder[right.tone])
    return map
  })

  const cityStates = computed<LiveCityChallengeState[]>(() => challenges.value.map(challenge => ({
    id: challengeKey(challenge.competitionChallengeId),
    title: challenge.title,
    score: challenge.currentScore,
    solveCount: challenge.solveCount,
    solvesText: `${challenge.solveCount} ${t("competitions.label.solve")}`,
    solved: challenge.solveCount > 0,
    bloods: bloodsByChallenge.value.get(challengeKey(challenge.competitionChallengeId)) ?? [],
  })))

  watch(cityStates, (states) => {
    scene?.setChallenges(states)
  }, { deep: true })

  function scheduleCelebrationEnd(): void {
    if (celebrationTimer) clearTimeout(celebrationTimer)
    celebrationTimer = setTimeout(() => {
      celebrationTimer = undefined
      featuredSolve.value = null
      scene?.setLabelFocus(null)
      scene?.endFocus()
      celebrationTimer = setTimeout(() => {
        celebrationTimer = undefined
        playNextCelebration()
      }, 450)
    }, 5_200)
  }

  function focusSolve(solve: ControlScreenSolve): void {
    featuredSolve.value = solve
    const id = challengeKey(solve.competitionChallengeId)
    scene?.setLabelFocus(id)
    scene?.focus(id)
    scheduleCelebrationEnd()
  }

  function playNextCelebration(): void {
    if (featuredSolve.value || !celebrationQueue.value.length) return
    const next = celebrationQueue.value.shift()
    if (next) focusSolve(next)
  }

  function reconcileCelebrations(currentSolves: readonly ControlScreenSolve[]): void {
    const reconciled = reconcileControlScreenSolves(seenSolveKeys, currentSolves)
    seenSolveKeys = reconciled.seenKeys
    if (!reconciled.newSolves.length) return

    const activeChallengeId = challengeKey(featuredSolve.value?.competitionChallengeId)
    const sameChallengeUpdates = activeChallengeId
      ? reconciled.newSolves.filter(solve => challengeKey(solve.competitionChallengeId) === activeChallengeId)
      : []
    if (sameChallengeUpdates.length) focusSolve(sameChallengeUpdates.at(-1)!)

    celebrationQueue.value.push(...reconciled.newSolves.filter(
      solve => !activeChallengeId || challengeKey(solve.competitionChallengeId) !== activeChallengeId,
    ))
    playNextCelebration()
  }

  async function loadData(): Promise<void> {
    refreshing.value = Boolean(competition.value || board.snapshot.value)
    const competitionResult = await getCompetitionEndpoint({ path: { competitionId } })
    loading.value = false
    refreshing.value = false

    if (competitionResult.error || !competitionResult.data) {
      error.value = parseApiError(competitionResult.error, t("common.error.loadingCompetitionFailed")).displayMessage
      return
    }
    competition.value = competitionResult.data
    if (competition.value.mode !== 'Ctf') {
      projectionPending.value = false
      error.value = t("competitions.competitionsBy.description.dLiveScreenCurrently")
      return
    }
    await board.refresh({ catalog: true, schema: true, snapshot: true })
    if (board.processing.value) {
      error.value = null
      projectionPending.value = true
      if (!projectionTimer) {
        projectionTimer = setTimeout(() => {
          projectionTimer = undefined
          void refreshLatest()
        }, 2_000)
      }
      return
    }
    if (board.error.value || !board.snapshot.value) {
      projectionPending.value = false
      error.value = board.error.value ?? t("common.error.loadScoreboardFailed")
      return
    }
    projectionPending.value = false
    error.value = null
    await nextTick()
    reconcileCelebrations(allSolves.value)
  }

  const refreshLatest = createTrailingRefresh(loadData)

  async function toggleFullscreen(): Promise<void> {
    try {
      if (!document.fullscreenElement) await document.documentElement.requestFullscreen()
      else await document.exitFullscreen()
    }
    catch {
      toast.error(describeMessage("competitions.competitionsBy.description.browserDeniedFullscreenAccess"))
    }
  }

  function syncFullscreen(): void {
    fullscreen.value = Boolean(document.fullscreenElement)
  }

  onMounted(async () => {
    const sceneModule = arenaRef.value ? import('~/lib/live-city-3d') : null
    await ensureLoaded()
    await refreshLatest()
    if (sceneModule) {
      const { LiveCityScene } = await sceneModule
      if (disposed || !arenaRef.value) return
      scene = new LiveCityScene(arenaRef.value)
      scene.setChallenges(cityStates.value)
    }
    clockTimer = setInterval(() => { clock.value = Date.now() }, 1000)
    refreshTimer = setInterval(() => void refreshLatest(), 15_000)
    document.addEventListener('fullscreenchange', syncFullscreen)
    unwatch = watchCompetition(competitionId, {
      scoreboardUpdated: () => void refreshLatest(),
      competitionLifecycleChanged: () => void refreshLatest(),
      onReconnected: () => void refreshLatest(),
    })
  })

  onUnmounted(() => {
    disposed = true
    if (clockTimer) clearInterval(clockTimer)
    if (refreshTimer) clearInterval(refreshTimer)
    if (projectionTimer) clearTimeout(projectionTimer)
    if (celebrationTimer) clearTimeout(celebrationTimer)
    document.removeEventListener('fullscreenchange', syncFullscreen)
    unwatch?.()
    scene?.dispose()
    scene = null
  })

  function setArenaRefRef(element: Element | ComponentPublicInstance | null) { arenaRef.value = element as typeof arenaRef.value }

  return {
      Clock3,
      Expand,
      Minimize,
      Radio,
      RefreshCw,
      ShieldCheck,
      Trophy,
      Users,
      X,
      scoreboardRankingStateLabel,
      scoreboardTeamSolveCount,
      competitionId,
      configuration,
      t,
      board,
      competition,
      loading,
      refreshing,
      projectionPending,
      error,
      featuredSolve,
      fullscreen,
      arenaRef,
      entries,
      rankedEntries,
      challenges,
      solveFeed,
      solvedChallengeCount,
      totalSolveCount,
      dataAsOf,
      marqueeEnabled,
      marqueeDuration,
      celebrationParticles,
      remainingText,
      elapsedText,
      bloodLabel,
      bloodClass,
      rankClass,
      refreshLatest,
      toggleFullscreen,
      setArenaRefRef
    }
}

export type CompetitionsByIdLivePageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdLivePage>>>
