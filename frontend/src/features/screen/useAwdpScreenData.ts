import type { Ref } from 'vue'
import type {
  AwdpChallengeStatus,
  AwdpRoundStat,
  AwdpScreenConnectionStatus,
  AwdpScreenEvent,
  AwdpScreenSnapshot,
  AwdpTeamChallengeState,
  AwdpTeamScore,
} from '@/types/awdpScreen'
import { computed, onMounted, onUnmounted, ref, shallowRef, watch } from 'vue'
import { awdpScreenApi, buildSafeEventMessage, parseAwdpScreenEvent, parseAwdpScreenSnapshot } from '@/api/awdpScreen'

type MaybeRef<T> = T | Ref<T>

interface UseAwdpScreenDataOptions {
  forceMock?: MaybeRef<boolean>
}

const MAX_EVENTS = 100
const MAX_ANIMATIONS = 20
const RECALIBRATION_MS = 5_000

export function useAwdpScreenData(gameId: Ref<string>, options: UseAwdpScreenDataOptions = {}) {
  const snapshot = shallowRef<AwdpScreenSnapshot | null>(null)
  const isLoading = ref(true)
  const error = ref<Error | null>(null)
  const connectionStatus = ref<AwdpScreenConnectionStatus>('connecting')
  const usingMock = ref(false)
  const lastSyncAt = ref<string | null>(null)
  const reconnectAttempts = ref(0)
  const animationEvents = ref<AwdpScreenEvent[]>([])
  const serverOffsetMs = ref(0)
  const clockTick = ref(Date.now())

  let eventSource: EventSource | null = null
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null
  let recalibrationTimer: ReturnType<typeof setInterval> | null = null
  let clockTimer: ReturnType<typeof setInterval> | null = null
  let snapshotController: AbortController | null = null

  const game = computed(() => snapshot.value?.game ?? null)
  const stats = computed(() => snapshot.value?.stats ?? null)
  const scoreboard = computed(() => snapshot.value?.scoreboard ?? [])
  const challenges = computed(() => snapshot.value?.challenges ?? [])
  const teamChallengeStates = computed(() => snapshot.value?.teamChallengeStates ?? [])
  const recentEvents = computed(() => snapshot.value?.recentEvents ?? [])
  const roundTimeline = computed(() => snapshot.value?.roundTimeline ?? [])
  const forceMockSource = computed(() => Boolean(readMaybeRef(options.forceMock)))
  const remainingSeconds = computed(() => {
    if (!game.value?.roundEndsAt)
      return 0
    const serverNow = clockTick.value + serverOffsetMs.value
    return Math.max(0, Math.ceil((Date.parse(game.value.roundEndsAt) - serverNow) / 1000))
  })

  function updateServerOffset(serverTime?: string) {
    if (!serverTime)
      return
    const parsed = Date.parse(serverTime)
    if (Number.isFinite(parsed)) {
      serverOffsetMs.value = parsed - Date.now()
    }
  }

  function clearReconnectTimer() {
    if (reconnectTimer)
      clearTimeout(reconnectTimer)
    reconnectTimer = null
  }

  function clearRecalibrationTimer() {
    if (recalibrationTimer)
      clearInterval(recalibrationTimer)
    recalibrationTimer = null
  }

  async function refreshSnapshot() {
    await loadSnapshot(false)
    restartTransport()
  }

  async function loadSnapshot(silent: boolean) {
    const id = gameId.value
    if (!id)
      return

    if (!silent) {
      isLoading.value = true
      error.value = null
    }

    snapshotController?.abort()
    snapshotController = new AbortController()

    if (mockMode() === 'force') {
      // Force-mock only engages in dev (callers gate on `import.meta.env.DEV`).
      // The specifier must be a literal so Vite resolves the `@` alias — a
      // variable specifier with `@vite-ignore` reaches the browser
      // untransformed and fails to resolve. In production builds the DEV check
      // below lets Rollup tree-shake the mock chunk.
      if (!import.meta.env.DEV) return
      const { createMockAwdpScreenSnapshot } = await import('@/mocks/awdpScreenMock')
      applySnapshot(createMockAwdpScreenSnapshot(id), true)
      isLoading.value = false
      return
    }

    try {
      const data = await awdpScreenApi.snapshot(id, snapshotController.signal)
      applySnapshot(data, false)
      error.value = null
    }
    catch (err) {
      if (!silent || !snapshot.value)
        error.value = err instanceof Error ? err : new Error(String(err))
      connectionStatus.value = snapshot.value ? 'reconnecting' : 'disconnected'
    }
    finally {
      isLoading.value = false
    }
  }

  function applySnapshot(data: AwdpScreenSnapshot, mock: boolean) {
    const safeEvents = (data.recentEvents ?? [])
      .map(sanitizeEvent)
      .slice(0, MAX_EVENTS)

    snapshot.value = {
      ...data,
      stats: {
        ...data.stats,
        totalScoreDelta: data.stats.totalScoreDelta ?? sumTimelineScore(data.roundTimeline ?? []),
      },
      scoreboard: normalizeScoreboard(data.scoreboard ?? []),
      challenges: data.challenges ?? [],
      teamChallengeStates: normalizeTeamChallengeStates(data.teamChallengeStates ?? []),
      recentEvents: safeEvents,
      roundTimeline: data.roundTimeline ?? [],
    }

    usingMock.value = mock
    connectionStatus.value = 'connected'
    lastSyncAt.value = new Date().toISOString()
    updateServerOffset(snapshot.value.game.serverTime)
  }

  function applyEvent(rawEvent: AwdpScreenEvent) {
    const current = snapshot.value
    if (!current)
      return

    const event = sanitizeEvent(rawEvent)
    const scoreboardBefore = new Map(current.scoreboard.map(team => [team.teamId, team.rank]))
    const next: AwdpScreenSnapshot = {
      game: {
        ...current.game,
        serverTime: new Date(Date.now() + serverOffsetMs.value).toISOString(),
      },
      stats: { ...current.stats },
      scoreboard: current.scoreboard.map(team => ({ ...team })),
      challenges: current.challenges.map(challenge => ({ ...challenge })),
      teamChallengeStates: current.teamChallengeStates.map(state => ({ ...state })),
      recentEvents: [event, ...current.recentEvents].slice(0, MAX_EVENTS),
      roundTimeline: current.roundTimeline.map(round => ({ ...round })),
    }

    updateStats(next, event)
    updateTeam(next.scoreboard, event)
    updateChallenge(next.challenges, event, next.stats.teamCount)
    updateTimeline(next.roundTimeline, event)
    refreshRanks(next.scoreboard, scoreboardBefore)
    next.stats.activeTeamCount = activeTeamCount(next.scoreboard)
    next.stats.activeChallengeCount = activeChallengeCount(next.challenges)
    next.stats.totalScoreDelta = sumTimelineScore(next.roundTimeline)

    snapshot.value = next
    animationEvents.value = [event, ...animationEvents.value].slice(0, MAX_ANIMATIONS)
  }

  function restartTransport() {
    stopTransport()
    if (!snapshot.value)
      return

    if (usingMock.value)
      return

    startEventSource()
  }

  function startEventSource() {
    if (typeof EventSource === 'undefined') {
      connectionStatus.value = snapshot.value ? 'connected' : 'disconnected'
      return
    }

    connectionStatus.value = reconnectAttempts.value > 0 ? 'reconnecting' : 'connecting'
    eventSource = new EventSource(awdpScreenApi.eventStreamUrl(gameId.value), { withCredentials: true })

    eventSource.onopen = () => {
      connectionStatus.value = 'connected'
      reconnectAttempts.value = 0
      void loadSnapshot(true)
    }

    eventSource.onmessage = (message) => {
      handleStreamPayload(message.data)
    }

    eventSource.addEventListener('awdp-screen-event', (message) => {
      handleStreamPayload((message as MessageEvent<string>).data)
    })

    eventSource.onerror = () => {
      eventSource?.close()
      eventSource = null
      scheduleReconnect()
    }
  }

  function handleStreamPayload(data: string) {
    const nextSnapshot = parseAwdpScreenSnapshot(data)
    if (nextSnapshot) {
      applySnapshot(nextSnapshot, false)
      return
    }

    const event = parseAwdpScreenEvent(data)
    if (event)
      applyEvent(event)
  }

  function scheduleReconnect() {
    connectionStatus.value = 'reconnecting'
    reconnectAttempts.value += 1
    clearReconnectTimer()
    const delay = Math.min(10_000, 1_000 * 2 ** Math.min(reconnectAttempts.value, 4))
    reconnectTimer = setTimeout(() => {
      void loadSnapshot(true)
      startEventSource()
    }, delay)
  }

  function startRecalibration() {
    clearRecalibrationTimer()
    recalibrationTimer = setInterval(() => {
      void loadSnapshot(true)
    }, RECALIBRATION_MS)
  }

  function stopTransport() {
    eventSource?.close()
    eventSource = null
    clearReconnectTimer()
  }

  function startClock() {
    clockTimer = setInterval(() => {
      clockTick.value = Date.now()
    }, 1_000)
  }

  function stopAll() {
    snapshotController?.abort()
    snapshotController = null
    stopTransport()
    clearRecalibrationTimer()
    if (clockTimer)
      clearInterval(clockTimer)
    clockTimer = null
  }

  function mockMode() {
    if (readMaybeRef(options.forceMock))
      return 'force'
    return 'off'
  }

  watch(
    [gameId, forceMockSource],
    async () => {
      stopTransport()
      await loadSnapshot(false)
      restartTransport()
    },
    { immediate: true },
  )

  onMounted(() => {
    startClock()
    startRecalibration()
  })

  onUnmounted(() => {
    stopAll()
  })

  return {
    snapshot,
    game,
    stats,
    scoreboard,
    challenges,
    teamChallengeStates,
    recentEvents,
    roundTimeline,
    remainingSeconds,
    animationEvents,
    isLoading,
    error,
    connectionStatus,
    lastSyncAt,
    reconnectAttempts,
    refreshSnapshot,
  }
}

function sanitizeEvent(event: AwdpScreenEvent): AwdpScreenEvent {
  return {
    ...event,
    message: buildSafeEventMessage(event),
  }
}

function normalizeScoreboard(scoreboard: AwdpTeamScore[]) {
  return [...scoreboard]
    .sort((a, b) => a.rank - b.rank || b.totalScore - a.totalScore)
    .map((team, index) => ({
      ...team,
      rank: index + 1,
      trend: team.trend ?? 'stable',
    }))
}

function normalizeTeamChallengeStates(states: AwdpTeamChallengeState[]) {
  return [...states].sort((a, b) =>
    a.rank - b.rank
    || a.challengeName.localeCompare(b.challengeName)
    || a.teamName.localeCompare(b.teamName),
  )
}

function updateStats(snapshot: AwdpScreenSnapshot, event: AwdpScreenEvent) {
  if (event.type === 'ATTACK_ACCEPTED') {
    snapshot.stats.attackSuccessCount += 1
    snapshot.stats.totalAttackCount += 1
  }
  if (event.type === 'ATTACK_REJECTED') {
    snapshot.stats.attackFailCount += 1
    snapshot.stats.totalAttackCount += 1
  }
  if (event.type === 'DEFENSE_CHECK_PASSED') {
    snapshot.stats.defenseSuccessCount += 1
    snapshot.stats.totalDefenseCount += 1
  }
  if (event.type === 'DEFENSE_CHECK_FAILED') {
    snapshot.stats.defenseFailCount += 1
    snapshot.stats.totalDefenseCount += 1
  }
}

function updateTeam(scoreboard: AwdpTeamScore[], event: AwdpScreenEvent) {
  if (!event.teamId)
    return
  const team = scoreboard.find(item => item.teamId === event.teamId)
  if (!team)
    return

  team.lastActiveAt = event.createdAt
  if (typeof event.scoreDelta !== 'number')
    return

  team.totalScore += event.scoreDelta
  team.currentRoundScore += event.scoreDelta
  if (event.type === 'ATTACK_ACCEPTED')
    team.attackScore += event.scoreDelta
  if (event.type === 'DEFENSE_CHECK_PASSED')
    team.defenseScore += event.scoreDelta
}

function updateChallenge(challenges: AwdpChallengeStatus[], event: AwdpScreenEvent, teamCount: number) {
  if (!event.challengeId)
    return
  const challenge = challenges.find(item => item.challengeId === event.challengeId)
  if (!challenge)
    return

  challenge.lastEventAt = event.createdAt
  if (event.type === 'ATTACK_ACCEPTED' || event.type === 'ATTACK_REJECTED' || event.type === 'ATTACK_SUBMITTED') {
    challenge.attackHeat = Math.min(100, challenge.attackHeat + 4)
  }
  if (event.type === 'DEFENSE_CHECK_PASSED')
    challenge.defensePassedCount += 1
  if (event.type === 'DEFENSE_CHECK_FAILED')
    challenge.defenseFailedCount += 1
  if (event.type === 'INSTANCE_CREATED')
    challenge.instanceCount += 1
  if (event.teamId)
    challenge.activeTeamCount = Math.min(teamCount, challenge.activeTeamCount + 1)
}

function updateTimeline(timeline: AwdpRoundStat[], event: AwdpScreenEvent) {
  let round = timeline.find(item => item.round === event.round)
  if (!round) {
    round = {
      round: event.round,
      attackSuccessCount: 0,
      defenseSuccessCount: 0,
      attackFailCount: 0,
      defenseFailCount: 0,
      scoreDelta: 0,
      activeTeamCount: 0,
    }
    timeline.push(round)
    timeline.sort((a, b) => a.round - b.round)
  }

  if (event.type === 'ATTACK_ACCEPTED')
    round.attackSuccessCount += 1
  if (event.type === 'ATTACK_REJECTED')
    round.attackFailCount += 1
  if (event.type === 'DEFENSE_CHECK_PASSED')
    round.defenseSuccessCount += 1
  if (event.type === 'DEFENSE_CHECK_FAILED')
    round.defenseFailCount += 1
  if (typeof event.scoreDelta === 'number')
    round.scoreDelta += event.scoreDelta
  if (event.teamId)
    round.activeTeamCount += 1
}

function refreshRanks(scoreboard: AwdpTeamScore[], previousRanks: Map<string, number>) {
  scoreboard.sort((a, b) => b.totalScore - a.totalScore)
  scoreboard.forEach((team, index) => {
    const previousRank = previousRanks.get(team.teamId) ?? team.rank
    team.previousRank = previousRank
    team.rank = index + 1
    team.trend = previousRank > team.rank ? 'up' : previousRank < team.rank ? 'down' : 'stable'
  })
}

function activeTeamCount(scoreboard: AwdpTeamScore[]) {
  const recentCutoff = Date.now() - 5 * 60 * 1000
  return scoreboard.filter(team => team.lastActiveAt && Date.parse(team.lastActiveAt) >= recentCutoff).length
}

function activeChallengeCount(challenges: AwdpChallengeStatus[]) {
  const recentCutoff = Date.now() - 5 * 60 * 1000
  return challenges.filter(challenge => challenge.lastEventAt && Date.parse(challenge.lastEventAt) >= recentCutoff).length
}

function sumTimelineScore(timeline: AwdpRoundStat[]) {
  return timeline.reduce((sum, round) => sum + round.scoreDelta, 0)
}

function readMaybeRef<T>(value: MaybeRef<T> | undefined) {
  if (!value)
    return undefined
  return typeof value === 'object' && 'value' in value ? value.value : value
}
