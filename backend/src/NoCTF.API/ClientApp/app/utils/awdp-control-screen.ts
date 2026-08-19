import type {
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardEntryResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse,
} from '~/api'

export type AwdpControlAction = 'attack' | 'defense'
export type AwdpControlOutcome = 'pending' | 'success' | 'failure'

export interface AwdpControlEvent {
  id: string
  action: AwdpControlAction
  outcome: AwdpControlOutcome
  teamId: string
  teamName: string
  competitionChallengeId: string
  challengeTitle: string
  occurredAt: string
  gameplayFactId: string | null
}

export interface AwdpControlEventReconciliation {
  seenIds: Set<string>
  newEvents: AwdpControlEvent[]
}

export interface AwdpOperationMetric {
  success: number
  total: number
}

export interface AwdpOperationMetrics {
  attack: AwdpOperationMetric
  defense: AwdpOperationMetric
}

export interface AwdpRoundClock {
  currentRound: number
  remainingSeconds: number
}

export interface AwdpTeamChallengeState {
  competitionChallengeId: string
  title: string
  direction: string
  attackScore: number
  defenseScore: number
  attackOutcome: AwdpControlOutcome | 'idle'
  defenseOutcome: AwdpControlOutcome | 'idle'
}

export interface AwdpRankedEntry extends NoCtfapiEndpointsCompetitionsLeaderboardEntryResponse {
  rank: number
  trend: 'up' | 'down' | 'steady'
}

const AWDP_EVENT_KINDS = new Set([
  'AwdpBreakAttempted',
  'AwdpFixAttempted',
  'AwdpBreakResolved',
  'AwdpFixResolved',
])

export const awdpControlEventKinds = [
  'AwdpBreakAttempted',
  'AwdpFixAttempted',
  'AwdpBreakResolved',
  'AwdpFixResolved',
] as const

export function normalizeAwdpControlEvent(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): AwdpControlEvent | null {
  if (!event.kind || !AWDP_EVENT_KINDS.has(event.kind)) return null
  if (!event.id || !event.occurredAt || !event.teamId || !event.competitionChallengeId) return null

  const action: AwdpControlAction = event.kind.includes('Break') ? 'attack' : 'defense'
  const resolved = event.kind.endsWith('Resolved')
  const outcome: AwdpControlOutcome = !resolved
    ? 'pending'
    : event.gameplayFactState === 'Completed' && event.gameplayFactResult === 'Correct'
      ? 'success'
      : 'failure'

  return {
    id: event.id,
    action,
    outcome,
    teamId: event.teamId,
    teamName: event.teamDisplayName?.trim() || '—',
    competitionChallengeId: event.competitionChallengeId,
    challengeTitle: event.challengeTitle?.trim() || '—',
    occurredAt: event.occurredAt,
    gameplayFactId: event.gameplayFactId ?? null,
  }
}

export function awdpControlEvents(
  events: readonly NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[],
): AwdpControlEvent[] {
  return events
    .map(normalizeAwdpControlEvent)
    .filter((event): event is AwdpControlEvent => event !== null)
    .sort((left, right) => left.occurredAt.localeCompare(right.occurredAt) || left.id.localeCompare(right.id))
}

export function reconcileAwdpControlEvents(
  previousIds: ReadonlySet<string> | null,
  currentEvents: readonly AwdpControlEvent[],
): AwdpControlEventReconciliation {
  const seenIds = new Set(previousIds ?? [])
  const newEvents = previousIds
    ? currentEvents.filter(event => !seenIds.has(event.id))
    : []
  for (const event of currentEvents) seenIds.add(event.id)
  return { seenIds, newEvents }
}

export function awdpPlaybackEvents(events: readonly AwdpControlEvent[]): AwdpControlEvent[] {
  return events.filter(event => event.outcome !== 'pending')
}

export function awdpOperationMetrics(events: readonly AwdpControlEvent[]): AwdpOperationMetrics {
  const attempts = new Map<string, AwdpControlEvent>()
  for (const event of events) {
    const key = `${event.action}:${event.gameplayFactId ?? event.id}`
    const previous = attempts.get(key)
    if (!previous || previous.outcome === 'pending' || event.outcome !== 'pending')
      attempts.set(key, event)
  }
  const metric = (action: AwdpControlAction): AwdpOperationMetric => {
    const matching = [...attempts.values()].filter(event => event.action === action)
    return {
      success: matching.filter(event => event.outcome === 'success').length,
      total: matching.length,
    }
  }
  return { attack: metric('attack'), defense: metric('defense') }
}

export function awdpRoundClock(
  leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null,
  now: number,
  advances: boolean,
): AwdpRoundClock {
  const currentRound = Math.max(0, leaderboard?.currentRound ?? 0)
  const duration = leaderboard?.roundDurationSeconds ?? 0
  const baseRemaining = leaderboard?.currentRoundRemainingSeconds ?? 0
  if (!currentRound || duration <= 0 || baseRemaining <= 0)
    return { currentRound, remainingSeconds: Math.max(0, baseRemaining) }

  const generatedAt = leaderboard?.generatedAt
    ? new Date(leaderboard.generatedAt).getTime()
    : now
  const elapsed = advances && Number.isFinite(generatedAt)
    ? Math.max(0, Math.floor((now - generatedAt) / 1000))
    : 0
  const elapsedInBaseRound = duration - Math.min(duration, baseRemaining)
  const totalElapsed = elapsedInBaseRound + elapsed
  return {
    currentRound: currentRound + Math.floor(totalElapsed / duration),
    remainingSeconds: duration - totalElapsed % duration,
  }
}

export function awdpPublicEntries(
  leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null,
): NoCtfapiEndpointsCompetitionsLeaderboardEntryResponse[] {
  const publicTrackKeys = new Set(
    (leaderboard?.tracks ?? [])
      .filter(track => track.isInternal !== true)
      .map(track => track.key)
      .filter((key): key is string => Boolean(key)),
  )
  return (leaderboard?.entries ?? []).filter(entry =>
    !entry.trackKey || publicTrackKeys.has(entry.trackKey))
}

export function awdpRankedEntries(
  leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null,
  previousRanks: ReadonlyMap<string, number> = new Map(),
): AwdpRankedEntry[] {
  return awdpPublicEntries(leaderboard)
    .sort((left, right) =>
      (left.rank ?? Number.MAX_SAFE_INTEGER) - (right.rank ?? Number.MAX_SAFE_INTEGER)
      || (right.score ?? 0) - (left.score ?? 0)
      || (left.teamName ?? '').localeCompare(right.teamName ?? ''))
    .map((entry, index) => {
      const rank = entry.rank ?? index + 1
      const previous = entry.teamId ? previousRanks.get(entry.teamId) : undefined
      return {
        ...entry,
        rank,
        trend: previous === undefined || previous === rank
          ? 'steady'
          : rank < previous ? 'up' : 'down',
      }
    })
}

function latestOutcome(
  events: readonly AwdpControlEvent[],
  teamId: string,
  challengeId: string,
  action: AwdpControlAction,
): AwdpControlOutcome | 'idle' {
  for (let index = events.length - 1; index >= 0; index -= 1) {
    const event = events[index]
    if (event?.teamId === teamId
      && event.competitionChallengeId === challengeId
      && event.action === action)
      return event.outcome
  }
  return 'idle'
}

export function awdpTeamChallengeStates(
  leaderboard: NoCtfapiEndpointsCompetitionsLeaderboardProtocolResponse | null,
  entry: NoCtfapiEndpointsCompetitionsLeaderboardEntryResponse | null,
  events: readonly AwdpControlEvent[],
): AwdpTeamChallengeState[] {
  if (!entry?.teamId) return []
  return (leaderboard?.challenges ?? []).map((challenge) => {
    const challengeId = challenge.competitionChallengeId ?? ''
    const cell = (entry.cells ?? []).find(item => item.competitionChallengeId === challengeId)
    const attackScore = cell?.attackScore ?? 0
    const defenseScore = cell?.defenseScore ?? 0
    const attackOutcome = latestOutcome(events, entry.teamId!, challengeId, 'attack')
    const defenseOutcome = latestOutcome(events, entry.teamId!, challengeId, 'defense')
    return {
      competitionChallengeId: challengeId,
      title: challenge.title ?? '—',
      direction: challenge.direction ?? 'MISC',
      attackScore,
      defenseScore,
      attackOutcome: attackOutcome === 'idle' && attackScore > 0 ? 'success' : attackOutcome,
      defenseOutcome: defenseOutcome === 'idle' && defenseScore > 0 ? 'success' : defenseOutcome,
    }
  })
}

export function calculateAwdpCanvasScale(
  viewportWidth: number,
  viewportHeight: number,
  canvasWidth = 1920,
  canvasHeight = 1080,
): number {
  if (viewportWidth <= 0 || viewportHeight <= 0) return 1
  return Math.min(viewportWidth / canvasWidth, viewportHeight / canvasHeight)
}
