import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { Activity, ChevronLeft, ChevronRight, Clock3, Expand, Minimize, Radio, RefreshCw, ShieldCheck, Swords, Trophy, Users, X } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { getCompetitionEndpoint, listCompetitionEvents } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '../../../../api'
import { createTrailingRefresh } from '../../../../lib/latest-page-refresh'
import { awdpControlEventKinds, awdpControlEvents, awdpCurrentRoundEvents, awdpCurrentRoundOperationMetrics, awdpPlaybackEvents, awdpRankedEntries, awdpRoundClock, awdpTeamChallengeStates, calculateAwdpCanvasScale, reconcileAwdpControlEvents } from '../../../../utils/awdp-control-screen'
import type { AwdpControlEvent, AwdpRankedEntry, AwdpResolvedControlEvent } from '../../../../utils/awdp-control-screen'
import { directionIcon } from '../../../../utils/directions'
import { scoreboardRankingStateLabel } from '../../../../utils/scoreboard'
import AwdpEventStageComponent from '../../../awdp-control/AwdpEventStage.vue'
import AwdpEventTickerComponent from '../../../awdp-control/AwdpEventTicker.vue'

/** Owns state, effects and commands for CompetitionsByIdAwdpLivePage. */
export function useCompetitionsByIdAwdpLivePage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const { configuration, ensureLoaded } = usePlatform()

  const { t } = useLocale()

  const board = useScoreboardMatrix(competitionId, { pollRounds: false })

  const competition = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>(null)

  const events = ref<AwdpControlEvent[]>([])

  const loading = ref(true)

  const refreshing = ref(false)

  const projectionPending = ref(false)

  const error = ref<string | null>(null)

  const activeEvent = ref<AwdpResolvedControlEvent | null>(null)

  const playbackQueue = ref<AwdpResolvedControlEvent[]>([])

  const playbackProgress = ref(0)

  const clock = ref(Date.now())

  const fullscreen = ref(false)

  const viewportWidth = ref(1920)

  const viewportHeight = ref(1080)

  const selectedTeamIndex = ref(0)

  const teamPanelHovered = ref(false)

  const teamPanelFocused = ref(false)

  const teamCarouselPaused = computed(() => teamPanelHovered.value || teamPanelFocused.value)

  const previousRanks = new Map<string, number>()

  let seenEventIds: Set<string> | null = null

  let clockTimer: ReturnType<typeof setInterval> | undefined

  let refreshTimer: ReturnType<typeof setInterval> | undefined

  let carouselTimer: ReturnType<typeof setInterval> | undefined

  let projectionTimer: ReturnType<typeof setTimeout> | undefined

  let playbackTimer: ReturnType<typeof setTimeout> | undefined

  let playbackFrame = 0

  let playbackStartedAt = 0

  let unwatch: (() => void) | undefined

  const PLAYBACK_DURATION_MS = 5_400

  const PLAYBACK_GAP_MS = 320

  const rankedEntries = computed(() => awdpRankedEntries(board.snapshot.value, previousRanks))

  const topEntries = computed(() => rankedEntries.value.slice(0, 8))

  const selectedTeam = computed<AwdpRankedEntry | null>(() => {
    if (!rankedEntries.value.length) return null
    return rankedEntries.value[selectedTeamIndex.value % rankedEntries.value.length] ?? null
  })

  const currentRoundEvents = computed(() => awdpCurrentRoundEvents(
    events.value,
    board.snapshot.value,
    board.schema.value,
  ))

  const selectedChallengeStates = computed(() => {
    const states = awdpTeamChallengeStates(
      board.catalog.value,
      selectedTeam.value,
      currentRoundEvents.value,
    )
    const focusedChallengeId = activeEvent.value?.teamId === selectedTeam.value?.teamId
      ? activeEvent.value?.competitionChallengeId
      : null
    return focusedChallengeId
      ? [...states].sort((left, right) =>
          Number(right.competitionChallengeId === focusedChallengeId)
          - Number(left.competitionChallengeId === focusedChallengeId))
      : states
  })

  const resolvedEvents = computed(() => awdpPlaybackEvents(events.value))

  const recentFeed = computed(() => [...resolvedEvents.value].reverse().slice(0, 13))

  const tickerEvents = computed(() => [...resolvedEvents.value].reverse().slice(0, 18))

  const canvasScale = computed(() => calculateAwdpCanvasScale(viewportWidth.value, viewportHeight.value))

  const canvasStyle = computed(() => ({
    transform: `translate(-50%, -50%) scale(${canvasScale.value})`,
  }))

  const liveRoundClock = computed(() => awdpRoundClock(
    board.snapshot.value,
    board.schema.value,
    clock.value,
    competition.value?.status === 'Running',
  ))

  const currentRound = computed(() => liveRoundClock.value.currentRound)

  const settledRound = computed(() => Math.max(0, ...(board.schema.value?.rounds ?? [])
    .filter(round => round.state === 'Settled')
    .map(round => round.number ?? 0)))

  const operationMetrics = computed(() => awdpCurrentRoundOperationMetrics(
    board.snapshot.value,
    board.schema.value,
  ))

  const selectedTeamMetrics = computed(() => awdpCurrentRoundOperationMetrics(
    board.snapshot.value,
    board.schema.value,
    selectedTeam.value?.teamId,
  ))

  const remainingText = computed(() => {
    if (competition.value?.status === 'Finished') return t("ui.competitionFinished")
    const total = liveRoundClock.value.remainingSeconds
    if (!total) return '—'
    const hours = Math.floor(total / 3600)
    const minutes = Math.floor(total % 3600 / 60)
    const seconds = total % 60
    return `${hours.toString().padStart(2, '0')}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
  })

  function resizeCanvas(): void {
    viewportWidth.value = window.innerWidth
    viewportHeight.value = window.innerHeight
  }

  function rankTone(rank: number | null | undefined): string {
    if (rank === 1) return 'gold'
    if (rank === 2) return 'silver'
    if (rank === 3) return 'bronze'
    return 'normal'
  }

  function eventLabel(event: AwdpResolvedControlEvent): string {
    const action = event.action === 'attack' ? t("ui.attack") : t("ui.defense")
    const outcome = event.outcome === 'success' ? t("ui.success") : t("ui.failed")
    return `${action}${outcome}`
  }

  function eventTime(value?: string | null): string {
    if (!value) return '—'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? '—' : date.toLocaleTimeString(undefined, { hour12: false })
  }

  function progressPlayback(now: number): void {
    playbackProgress.value = Math.min(100, Math.max(0, (now - playbackStartedAt) / PLAYBACK_DURATION_MS * 100))
    if (activeEvent.value) playbackFrame = requestAnimationFrame(progressPlayback)
  }

  function playNext(): void {
    if (activeEvent.value || !playbackQueue.value.length) return
    const next = playbackQueue.value.shift()
    if (!next) return
    const teamIndex = rankedEntries.value.findIndex(entry => entry.teamId === next.teamId)
    if (teamIndex >= 0) selectedTeamIndex.value = teamIndex
    activeEvent.value = next
    playbackProgress.value = 0
    playbackStartedAt = performance.now()
    playbackFrame = requestAnimationFrame(progressPlayback)
    playbackTimer = setTimeout(() => {
      cancelAnimationFrame(playbackFrame)
      playbackProgress.value = 100
      activeEvent.value = null
      playbackTimer = setTimeout(() => {
        playbackTimer = undefined
        playNext()
      }, PLAYBACK_GAP_MS)
    }, PLAYBACK_DURATION_MS)
  }

  function isChallengeFocused(challengeId: string): boolean {
    return activeEvent.value?.teamId === selectedTeam.value?.teamId
      && activeEvent.value?.competitionChallengeId === challengeId
  }

  async function loadAllAwdpEvents(from: string, to: string): Promise<{
    data: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[] | null
    error: unknown
  }> {
    const items: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[] = []
    const seenCursors = new Set<string>()
    let cursor: string | null | undefined
    do {
      const result = await listCompetitionEvents({
        path: { competitionId },
        query: { from, to, kinds: [...awdpControlEventKinds], cursor, limit: 200 },
      })
      if (result.error || !result.data) return { data: null, error: result.error }
      items.push(...(result.data.items ?? []))
      cursor = result.data.nextCursor
      if (!cursor || seenCursors.has(cursor)) break
      seenCursors.add(cursor)
    } while (cursor)
    return { data: items, error: null }
  }

  function enqueueResolvedEvents(newEvents: readonly AwdpControlEvent[]): void {
    playbackQueue.value.push(...awdpPlaybackEvents(newEvents))
    playNext()
  }

  async function loadData(): Promise<void> {
    refreshing.value = Boolean(competition.value || board.snapshot.value)
    const competitionResult = await getCompetitionEndpoint({ path: { competitionId } })
    if (competitionResult.error || !competitionResult.data) {
      loading.value = false
      refreshing.value = false
      error.value = parseApiError(competitionResult.error, t("ui.loadingCompetitionFailed")).message
      return
    }
    competition.value = competitionResult.data
    if (competition.value.mode !== 'Awdp') {
      loading.value = false
      refreshing.value = false
      error.value = t("ui.theAwdpControlScreenIsOnlyAvailableForAwdpCompetitions")
      return
    }
  
    const now = Date.now()
    const knownStart = competition.value.startTime ? new Date(competition.value.startTime).getTime() : now
    const from = new Date(Math.max(knownStart, now - 31 * 24 * 60 * 60 * 1000)).toISOString()
    const to = new Date(now).toISOString()
    const [, eventResult] = await Promise.all([
      board.refresh({ catalog: true, schema: true, snapshot: true }),
      loadAllAwdpEvents(from, to),
    ])
    loading.value = false
    refreshing.value = false
  
    if (eventResult.error || !eventResult.data) {
      error.value = parseApiError(eventResult.error, t("ui.failedToLoadCompetitionActivity")).message
      return
    }
    const nextEvents = awdpControlEvents(eventResult.data)
    const reconciliation = reconcileAwdpControlEvents(seenEventIds, nextEvents)
    seenEventIds = reconciliation.seenIds
    events.value = nextEvents
    enqueueResolvedEvents(reconciliation.newEvents)
  
    if (board.processing.value) {
      projectionPending.value = true
      error.value = null
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
      error.value = board.error.value ?? t("ui.failedToLoadScoreboard")
      return
    }
    projectionPending.value = false
    error.value = null
    for (const entry of awdpRankedEntries(board.snapshot.value)) {
      if (entry.teamId && entry.rank !== null) previousRanks.set(entry.teamId, entry.rank)
    }
    if (selectedTeamIndex.value >= rankedEntries.value.length) selectedTeamIndex.value = 0
  }

  const refreshLatest = createTrailingRefresh(loadData)

  function selectPreviousTeam(): void {
    if (!rankedEntries.value.length) return
    selectedTeamIndex.value = (selectedTeamIndex.value - 1 + rankedEntries.value.length) % rankedEntries.value.length
  }

  function selectNextTeam(): void {
    if (!rankedEntries.value.length) return
    selectedTeamIndex.value = (selectedTeamIndex.value + 1) % rankedEntries.value.length
  }

  async function toggleFullscreen(): Promise<void> {
    try {
      if (!document.fullscreenElement) await document.documentElement.requestFullscreen()
      else await document.exitFullscreen()
    }
    catch {
      toast.error(translate("ui.theBrowserDeniedFullscreenAccessCheckSitePermissionsOrUse"))
    }
  }

  function advanceTeamCarousel(): void {
    if (!teamCarouselPaused.value) selectNextTeam()
  }

  function syncFullscreen(): void { fullscreen.value = Boolean(document.fullscreenElement) }

  onMounted(async () => {
    resizeCanvas()
    window.addEventListener('resize', resizeCanvas)
    document.addEventListener('fullscreenchange', syncFullscreen)
    await ensureLoaded()
    await refreshLatest()
    clockTimer = setInterval(() => { clock.value = Date.now() }, 1_000)
    refreshTimer = setInterval(() => void refreshLatest(), 10_000)
    carouselTimer = setInterval(advanceTeamCarousel, 8_000)
    unwatch = watchCompetition(competitionId, {
      scoreboardUpdated: () => void refreshLatest(),
      competitionEventChanged: () => void refreshLatest(),
      competitionLifecycleChanged: () => void refreshLatest(),
      onReconnected: () => void refreshLatest(),
    })
  })

  onUnmounted(() => {
    if (clockTimer) clearInterval(clockTimer)
    if (refreshTimer) clearInterval(refreshTimer)
    if (carouselTimer) clearInterval(carouselTimer)
    if (projectionTimer) clearTimeout(projectionTimer)
    if (playbackTimer) clearTimeout(playbackTimer)
    cancelAnimationFrame(playbackFrame)
    window.removeEventListener('resize', resizeCanvas)
    document.removeEventListener('fullscreenchange', syncFullscreen)
    unwatch?.()
  })

  const AwdpEventStage = markRaw(AwdpEventStageComponent)

  const AwdpEventTicker = markRaw(AwdpEventTickerComponent)

  const viewBindings = {
      Activity,
      ChevronLeft,
      ChevronRight,
      Clock3,
      Expand,
      Minimize,
      Radio,
      RefreshCw,
      ShieldCheck,
      Swords,
      Trophy,
      Users,
      X,
      directionIcon,
      scoreboardRankingStateLabel,
      competitionId,
      configuration,
      t,
      board,
      competition,
      events,
      loading,
      refreshing,
      projectionPending,
      error,
      activeEvent,
      playbackQueue,
      playbackProgress,
      clock,
      fullscreen,
      selectedTeamIndex,
      teamPanelHovered,
      teamPanelFocused,
      teamCarouselPaused,
      rankedEntries,
      topEntries,
      selectedTeam,
      selectedChallengeStates,
      recentFeed,
      tickerEvents,
      canvasStyle,
      currentRound,
      settledRound,
      operationMetrics,
      selectedTeamMetrics,
      remainingText,
      rankTone,
      eventLabel,
      eventTime,
      isChallengeFocused,
      refreshLatest,
      selectPreviousTeam,
      selectNextTeam,
      toggleFullscreen,
      AwdpEventStage,
      AwdpEventTicker
    }
  const viewState = proxyRefs(viewBindings)

  function onMouseenterTeamPanelHovered(value: typeof viewState.teamPanelHovered) {
    viewState.teamPanelHovered = value
  }

  function onMouseleaveTeamPanelHovered(value: typeof viewState.teamPanelHovered) {
    viewState.teamPanelHovered = value
  }

  function onFocusinTeamPanelFocused(value: typeof viewState.teamPanelFocused) {
    viewState.teamPanelFocused = value
  }

  function onFocusoutTeamPanelFocused(value: typeof viewState.teamPanelFocused) {
    viewState.teamPanelFocused = value
  }

  function onClickSelectedTeamIndex(value: typeof viewState.selectedTeamIndex) {
    viewState.selectedTeamIndex = value
  }

  return { ...viewBindings, onMouseenterTeamPanelHovered, onMouseleaveTeamPanelHovered, onFocusinTeamPanelFocused, onFocusoutTeamPanelFocused, onClickSelectedTeamIndex }
}

export type CompetitionsByIdAwdpLivePageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdAwdpLivePage>>>
