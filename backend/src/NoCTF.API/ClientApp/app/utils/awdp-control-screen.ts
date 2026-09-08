import type {
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
  NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
  NoCtfapiEndpointsCompetitionsScoreboardTeamResponse,
} from '../api'
import { scoreboardBreakdown } from './scoreboard'
import { directionLabel } from './directions'

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

export type AwdpResolvedControlEvent = Omit<AwdpControlEvent, 'outcome'> & {
  outcome: Exclude<AwdpControlOutcome, 'pending'>
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

export interface AwdpRankedEntry extends NoCtfapiEndpointsCompetitionsScoreboardTeamResponse {
  rank: number | null
  attackScore: number
  defenseScore: number
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

export function awdpPlaybackEvents(events: readonly AwdpControlEvent[]): AwdpResolvedControlEvent[] {
  return events.filter((event): event is AwdpResolvedControlEvent => event.outcome !== 'pending')
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

export function awdpCurrentRoundEvents(
  events: readonly AwdpControlEvent[],
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
): AwdpControlEvent[] {
  const round = (schema?.rounds ?? []).find(item => item.id === snapshot?.currentRoundId)
  const startAt = round?.startAt ? new Date(round.startAt).getTime() : Number.NaN
  const endAt = round?.endAt ? new Date(round.endAt).getTime() : Number.NaN
  if (!Number.isFinite(startAt) || !Number.isFinite(endAt)) return []
  return events.filter((event) => {
    const occurredAt = new Date(event.occurredAt).getTime()
    return Number.isFinite(occurredAt) && occurredAt >= startAt && occurredAt < endAt
  })
}

export function awdpCurrentRoundOperationMetrics(
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  teamId?: string,
): AwdpOperationMetrics {
  const columnIndexes = new Set((schema?.columns ?? [])
    .filter(column => column.roundId === snapshot?.currentRoundId && column.index !== undefined)
    .map(column => column.index!))
  const metrics: AwdpOperationMetrics = {
    attack: { success: 0, total: 0 },
    defense: { success: 0, total: 0 },
  }
  if (!columnIndexes.size) return metrics
  const teams = awdpPublicEntries(snapshot).filter(entry => !teamId || entry.teamId === teamId)
  for (const entry of teams) {
    for (const slot of entry.slots ?? []) {
      if (slot.columnIndex === undefined || !columnIndexes.has(slot.columnIndex)) continue
      const attack = scoreboardBreakdown(slot, 'Attack')
      const defense = scoreboardBreakdown(slot, 'Defense')
      metrics.attack.success += attack?.successfulCount ?? 0
      metrics.attack.total += attack?.attemptCount ?? 0
      metrics.defense.success += defense?.successfulCount ?? 0
      metrics.defense.total += defense?.attemptCount ?? 0
    }
  }
  return metrics
}

export function awdpRoundClock(
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  now: number,
  advances: boolean,
): AwdpRoundClock {
  const round = (schema?.rounds ?? []).find(item => item.id === snapshot?.currentRoundId)
  const currentRound = Math.max(0, round?.number ?? 0)
  const endAt = round?.endAt ? new Date(round.endAt).getTime() : Number.NaN
  const startAt = round?.startAt ? new Date(round.startAt).getTime() : now
  if (!currentRound || !Number.isFinite(endAt) || round?.state === 'Settled')
    return { currentRound, remainingSeconds: 0 }
  const generatedAt = snapshot?.generatedAt ? new Date(snapshot.generatedAt).getTime() : Number.NaN
  const reference = advances
    ? now
    : Number.isFinite(generatedAt) ? generatedAt : Math.min(now, endAt)
  return {
    currentRound,
    remainingSeconds: Math.max(0, Math.floor((endAt - Math.max(reference, startAt)) / 1000)),
  }
}

export function awdpPublicEntries(
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
): NoCtfapiEndpointsCompetitionsScoreboardTeamResponse[] {
  const publicTrackKeys = new Set(
    (snapshot?.tracks ?? [])
      .filter(track => track.isInternal !== true)
      .map(track => track.key)
      .filter((key): key is string => Boolean(key)),
  )
  return (snapshot?.teams ?? []).filter(entry =>
    !entry.trackKey || publicTrackKeys.has(entry.trackKey))
}

export function awdpRankedEntries(
  snapshot: NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null,
  previousRanks: ReadonlyMap<string, number> = new Map(),
): AwdpRankedEntry[] {
  return awdpPublicEntries(snapshot)
    .map((entry) => {
      const rank = entry.rank ?? null
      const previous = entry.teamId ? previousRanks.get(entry.teamId) : undefined
      return {
        ...entry,
        rank,
        attackScore: entry.attackScore ?? 0,
        defenseScore: entry.defenseScore ?? 0,
        trend: rank === null || previous === undefined || previous === rank
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
  catalog: NoCtfapiEndpointsCompetitionsScoreboardChallengeCatalogResponse | null,
  schema: NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null,
  entry: NoCtfapiEndpointsCompetitionsScoreboardTeamResponse | null,
  events: readonly AwdpControlEvent[],
): AwdpTeamChallengeState[] {
  if (!entry?.teamId) return []
  return (catalog?.items ?? []).filter(challenge => challenge.id && challenge.published).map((challenge) => {
    const challengeId = challenge.id!
    const challengeScore = (entry.challengeScores ?? [])
      .find(score => score.competitionChallengeId === challengeId)
    const attackScore = challengeScore?.attackScore ?? 0
    const defenseScore = challengeScore?.defenseScore ?? 0
    const attackOutcome = latestOutcome(events, entry.teamId!, challengeId, 'attack')
    const defenseOutcome = latestOutcome(events, entry.teamId!, challengeId, 'defense')
    return {
      competitionChallengeId: challengeId,
      title: challenge.title ?? '—',
      direction: directionLabel(challenge.direction) || 'Misc',
      attackScore,
      defenseScore,
      attackOutcome,
      defenseOutcome,
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
