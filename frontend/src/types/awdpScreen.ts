export type AwdpGameStatus = 'pending' | 'running' | 'paused' | 'ended'

export type AwdpGamePhase = 'waiting' | 'running' | 'settling' | 'ended'

export type AwdpChallengeCategory = 'web' | 'pwn' | 'crypto' | 'reverse' | 'misc'

export type AwdpScreenEventType
  = | 'ROUND_STARTED'
    | 'ROUND_ENDED'
    | 'CHALLENGE_SELECTED'
    | 'INSTANCE_CREATED'
    | 'DEFENSE_REQUESTED'
    | 'PATCH_UPLOADED'
    | 'DEFENSE_CHECK_PASSED'
    | 'DEFENSE_CHECK_FAILED'
    | 'ATTACK_SUBMITTED'
    | 'ATTACK_ACCEPTED'
    | 'ATTACK_REJECTED'
    | 'SERVICE_ERROR'
    | 'SCORE_UPDATED'
    | 'TEAM_RANK_CHANGED'

export type AwdpScreenEventLevel = 'info' | 'success' | 'warning' | 'danger'

export type AwdpTeamTrend = 'up' | 'down' | 'stable'

export type AwdpInstanceStatus
  = | 'InstanceNotCreated'
    | 'InstanceCreating'
    | 'InstanceRunning'
    | 'InstanceExpired'
    | 'InstanceResetting'
    | string

export type AwdpBreakStatus
  = | 'BreakNotStarted'
    | 'BreakSubmitted'
    | 'BreakSuccess'
    | 'BreakFailed'
    | 'AttackAttemptsExhausted'
    | string

export type AwdpFixStatus
  = | 'FixNotStarted'
    | 'FixUploading'
    | 'FixAuditing'
    | 'FixRunning'
    | 'FixChecking'
    | 'FixSuccess'
    | 'FixFailed'
    | 'FixServiceError'
    | 'FixScriptError'
    | 'FixTimeout'
    | 'AuditFailed'
    | 'DefenseAttemptsExhausted'
    | 'FixRuleViolation'
    | string

export type AwdpServiceStatus
  = | 'ServiceUnknown'
    | 'ServiceOk'
    | 'ServiceError'
    | string

export type AwdpScreenConnectionStatus
  = | 'connecting'
    | 'connected'
    | 'reconnecting'
    | 'disconnected'

export interface AwdpScreenSnapshot {
  game: {
    id: string
    title: string
    status: AwdpGameStatus
    currentRound: number
    totalRounds: number
    phase: AwdpGamePhase
    serverTime: string
    roundStartedAt?: string
    roundEndsAt?: string
  }
  stats: {
    teamCount: number
    challengeCount: number
    totalAttackCount: number
    totalDefenseCount: number
    attackSuccessCount: number
    attackFailCount: number
    defenseSuccessCount: number
    defenseFailCount: number
    activeTeamCount: number
    activeChallengeCount: number
    totalScoreDelta?: number
  }
  scoreboard: AwdpTeamScore[]
  challenges: AwdpChallengeStatus[]
  teamChallengeStates: AwdpTeamChallengeState[]
  recentEvents: AwdpScreenEvent[]
  roundTimeline: AwdpRoundStat[]
}

export interface AwdpTeamScore {
  teamId: string
  teamName: string
  rank: number
  previousRank?: number
  totalScore: number
  attackScore: number
  defenseScore: number
  currentRoundScore: number
  lastActiveAt?: string
  trend: AwdpTeamTrend
}

export interface AwdpChallengeStatus {
  challengeId: string
  challengeName: string
  category: AwdpChallengeCategory
  attackHeat: number
  defensePassedCount: number
  defenseFailedCount: number
  instanceCount: number
  activeTeamCount: number
  lastEventAt?: string
}

export interface AwdpTeamChallengeState {
  teamId: string
  teamName: string
  rank: number
  challengeId: string
  challengeName: string
  instanceStatus: AwdpInstanceStatus
  breakStatus: AwdpBreakStatus
  fixStatus: AwdpFixStatus
  serviceStatus: AwdpServiceStatus
  attackAttempts: number
  defenseAttempts: number
  currentRoundScore: number
  lastActivityAt?: string
}

export interface AwdpRoundStat {
  round: number
  attackSuccessCount: number
  defenseSuccessCount: number
  attackFailCount: number
  defenseFailCount: number
  scoreDelta: number
  activeTeamCount: number
}

export interface AwdpScreenEvent {
  id: string
  gameId: string
  round: number
  type: AwdpScreenEventType
  teamId?: string
  teamName?: string
  challengeId?: string
  challengeName?: string
  challengeCategory?: AwdpChallengeCategory
  scoreDelta?: number
  level: AwdpScreenEventLevel
  message: string
  createdAt: string
}
