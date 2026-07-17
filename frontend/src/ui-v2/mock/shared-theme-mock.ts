import type {
  AwdpChallengeStatus,
  AwdpScreenSnapshot,
  AwdpTeamChallengeState,
} from '@/types/awdpScreen'

export interface CommandMetric {
  label: string
  value: string
  delta: string
  tone: 'primary' | 'success' | 'warning' | 'danger'
}

export interface CommandEvent {
  id: string
  time: string
  kind: string
  message: string
  tone: 'info' | 'success' | 'warning' | 'danger'
}

export interface CommandTeam {
  rank: number
  name: string
  score: number
  attack: number
  defense: number
  trend: 'up' | 'down' | 'steady'
}

export interface CommandService {
  name: string
  category: string
  status: 'stable' | 'degraded' | 'critical'
  activity: string
}

export interface CommandTrace {
  roundLabel: string
  serviceCount: number
  activeTeamCount: number
  incidentCount: number
  nodes: Pick<CommandService, 'name' | 'status'>[]
}

export interface CommandPreviewModel {
  metrics: CommandMetric[]
  events: CommandEvent[]
  teams: CommandTeam[]
  services: CommandService[]
  trace: CommandTrace
  snapshotTime: string
}

export async function loadSharedThemePreviewMock(): Promise<CommandPreviewModel | null> {
  // Match the first package's dev-only AWDP mock source. Keeping this import
  // runtime-gated prevents preview data from being emitted into production.
  const mockModulePath = import.meta.env.DEV ? '@/mocks/awdpScreenMock' : null
  if (!mockModulePath)
    return null

  const { createMockAwdpScreenSnapshot } = await import(/* @vite-ignore */ mockModulePath)
  return createCommandPreviewModel(createMockAwdpScreenSnapshot('comp-2026-awdp'))
}

export function createCommandPreviewModel(snapshot: AwdpScreenSnapshot): CommandPreviewModel {
  const services = snapshot.challenges.map(challenge => createCommandService(challenge, snapshot.teamChallengeStates))
  const incidentCount = services.filter(service => service.status === 'critical').length

  return {
    metrics: [
      {
        label: 'Current round',
        value: `${snapshot.game.currentRound}/${snapshot.game.totalRounds}`,
        delta: formatRemaining(snapshot.game.roundEndsAt),
        tone: 'primary',
      },
      {
        label: 'Active teams',
        value: String(snapshot.stats.activeTeamCount),
        delta: `${snapshot.stats.teamCount} registered`,
        tone: 'success',
      },
      {
        label: 'Attack accepted',
        value: snapshot.stats.attackSuccessCount.toLocaleString(),
        delta: `${snapshot.stats.totalAttackCount.toLocaleString()} total attempts`,
        tone: 'primary',
      },
      {
        label: 'Service alerts',
        value: String(incidentCount),
        delta: `${snapshot.stats.activeChallengeCount} challenges active`,
        tone: incidentCount > 0 ? 'danger' : 'success',
      },
    ],
    events: snapshot.recentEvents.slice(0, 5).map(event => ({
      id: event.id,
      time: formatTime(event.createdAt),
      kind: event.type.replaceAll('_', ' '),
      message: event.message,
      tone: event.level,
    })),
    teams: snapshot.scoreboard.slice(0, 5).map(team => ({
      rank: team.rank,
      name: team.teamName,
      score: team.totalScore,
      attack: team.attackScore,
      defense: team.defenseScore,
      trend: team.trend === 'stable' ? 'steady' : team.trend,
    })),
    services,
    trace: {
      roundLabel: `Round ${snapshot.game.currentRound}/${snapshot.game.totalRounds} / ${formatRemaining(snapshot.game.roundEndsAt)}`,
      serviceCount: services.length,
      activeTeamCount: snapshot.stats.activeTeamCount,
      incidentCount,
      nodes: services.slice(0, 4).map(({ name, status }) => ({ name, status })),
    },
    snapshotTime: formatTime(snapshot.game.serverTime),
  }
}

function createCommandService(
  challenge: AwdpChallengeStatus,
  states: AwdpTeamChallengeState[],
): CommandService {
  const challengeStates = states.filter(state => state.challengeId === challenge.challengeId)
  const hasServiceFailure = challengeStates.some(state => state.serviceStatus === 'ServiceError')
  const hasDefensePressure = challenge.defenseFailedCount > challenge.defensePassedCount

  return {
    name: challenge.challengeName,
    category: challenge.category.toUpperCase(),
    status: hasServiceFailure ? 'critical' : hasDefensePressure ? 'degraded' : 'stable',
    activity: `${challenge.activeTeamCount} teams / ${challenge.instanceCount} instances`,
  }
}

function formatTime(value: string) {
  const timestamp = Date.parse(value)
  if (!Number.isFinite(timestamp))
    return '--:--:--'

  return new Date(timestamp).toLocaleTimeString('en-GB', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatRemaining(endsAt?: string) {
  if (!endsAt)
    return 'time unavailable'

  const remainingSeconds = Math.max(0, Math.ceil((Date.parse(endsAt) - Date.now()) / 1000))
  const minutes = Math.floor(remainingSeconds / 60)
  const seconds = remainingSeconds % 60
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')} remaining`
}
