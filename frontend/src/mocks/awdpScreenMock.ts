import type {
  AwdpChallengeCategory,
  AwdpChallengeStatus,
  AwdpRoundStat,
  AwdpScreenEvent,
  AwdpScreenEventLevel,
  AwdpScreenEventType,
  AwdpScreenSnapshot,
  AwdpTeamChallengeState,
  AwdpTeamScore,
} from '@/types/awdpScreen'
import { buildSafeEventMessage } from '@/api/awdpScreen'

const teamNames = [
  'SnowTeam',
  'QAQTeam',
  'RedShift',
  'BlueBase',
  'NullCrew',
  'PatchLab',
  'CipherOps',
  'Kernel404',
  'ByteForge',
  'NeonRoot',
  'StackZero',
  'RelayOne',
]

const challengeSeeds: Array<{ name: string, category: AwdpChallengeCategory }> = [
  { name: 'WEB-upload', category: 'web' },
  { name: 'PWN-baby', category: 'pwn' },
  { name: 'WEB-easy', category: 'web' },
  { name: 'REV-portal', category: 'reverse' },
  { name: 'MISC-agent', category: 'misc' },
  { name: 'CRYPTO-lock', category: 'crypto' },
  { name: 'PWN-store', category: 'pwn' },
  { name: 'WEB-cache', category: 'web' },
]

const eventTypes: AwdpScreenEventType[] = [
  'ATTACK_ACCEPTED',
  'ATTACK_REJECTED',
  'DEFENSE_CHECK_PASSED',
  'DEFENSE_CHECK_FAILED',
  'SERVICE_ERROR',
  'INSTANCE_CREATED',
  'DEFENSE_REQUESTED',
  'PATCH_UPLOADED',
  'TEAM_RANK_CHANGED',
]

let eventCounter = 0

export function createMockAwdpScreenSnapshot(gameId: string): AwdpScreenSnapshot {
  const now = new Date()
  const roundDurationMs = 5 * 60 * 1000
  const currentRound = 12
  const roundStartedAt = new Date(now.getTime() - 155 * 1000)
  const roundEndsAt = new Date(roundStartedAt.getTime() + roundDurationMs)
  const scoreboard = createMockScoreboard(now)
  const challenges = createMockChallenges(now)
  const teamChallengeStates = createMockTeamChallengeStates(scoreboard, challenges, now)
  const roundTimeline = createMockTimeline(currentRound)
  const recentEvents = createInitialEvents(gameId, currentRound, scoreboard, challenges, now)

  return {
    game: {
      id: gameId,
      title: 'NoCTF AWDP Invitational',
      status: 'running',
      currentRound,
      totalRounds: 36,
      phase: 'running',
      serverTime: now.toISOString(),
      roundStartedAt: roundStartedAt.toISOString(),
      roundEndsAt: roundEndsAt.toISOString(),
    },
    stats: {
      teamCount: scoreboard.length,
      challengeCount: challenges.length,
      totalAttackCount: 286,
      totalDefenseCount: 194,
      attackSuccessCount: 132,
      attackFailCount: 154,
      defenseSuccessCount: 118,
      defenseFailCount: 76,
      activeTeamCount: 10,
      activeChallengeCount: 7,
      totalScoreDelta: roundTimeline.reduce((sum, item) => sum + item.scoreDelta, 0),
    },
    scoreboard,
    challenges,
    teamChallengeStates,
    recentEvents,
    roundTimeline,
  }
}

export function createMockAwdpScreenEvent(snapshot: AwdpScreenSnapshot): AwdpScreenEvent {
  const type = pick(eventTypes)
  const team = pick(snapshot.scoreboard)
  const challenge = pick(snapshot.challenges)
  const level = levelFor(type)
  const event: AwdpScreenEvent = {
    id: `mock-event-${Date.now()}-${eventCounter++}`,
    gameId: snapshot.game.id,
    round: snapshot.game.currentRound,
    type,
    teamId: team.teamId,
    teamName: team.teamName,
    challengeId: challenge.challengeId,
    challengeName: challenge.challengeName,
    challengeCategory: challenge.category,
    level,
    message: '',
    createdAt: new Date().toISOString(),
  }

  event.message = buildSafeEventMessage(event)
  return event
}

function createMockScoreboard(now: Date): AwdpTeamScore[] {
  return teamNames
    .map((teamName, index) => {
      const attackScore = 820 - index * 43 + randomInt(0, 80)
      const defenseScore = 610 - index * 31 + randomInt(0, 70)
      const currentRoundScore = randomInt(-20, 160)
      const previousRank = Math.max(1, index + 1 + randomInt(-1, 1))
      const rank = index + 1
      return {
        teamId: `mock-team-${index + 1}`,
        teamName,
        rank,
        previousRank,
        totalScore: attackScore + defenseScore + randomInt(0, 140),
        attackScore,
        defenseScore,
        currentRoundScore,
        lastActiveAt: new Date(now.getTime() - randomInt(15, 620) * 1000).toISOString(),
        trend: previousRank > rank ? 'up' : previousRank < rank ? 'down' : 'stable',
      } satisfies AwdpTeamScore
    })
    .sort((a, b) => b.totalScore - a.totalScore)
    .map((entry, index) => ({ ...entry, rank: index + 1 }))
}

function createMockChallenges(now: Date): AwdpChallengeStatus[] {
  return challengeSeeds.map((challenge, index) => ({
    challengeId: `mock-challenge-${index + 1}`,
    challengeName: challenge.name,
    category: challenge.category,
    attackHeat: randomInt(25, 96),
    defensePassedCount: randomInt(8, 34),
    defenseFailedCount: randomInt(1, 18),
    instanceCount: randomInt(4, 18),
    activeTeamCount: randomInt(2, 11),
    lastEventAt: new Date(now.getTime() - randomInt(25, 900) * 1000).toISOString(),
  }))
}

function createMockTeamChallengeStates(
  teams: AwdpTeamScore[],
  challenges: AwdpChallengeStatus[],
  now: Date,
): AwdpTeamChallengeState[] {
  return teams.flatMap(team => challenges.map((challenge, index) => {
    const breakStatus = pick(['BreakNotStarted', 'BreakSubmitted', 'BreakSuccess', 'BreakFailed', 'AttackAttemptsExhausted'])
    const fixStatus = pick(['FixNotStarted', 'FixUploading', 'FixSuccess', 'FixFailed', 'FixTimeout', 'AuditFailed'])
    const serviceStatus = pick(['ServiceOk', 'ServiceOk', 'ServiceError', 'ServiceUnknown'])

    return {
      teamId: team.teamId,
      teamName: team.teamName,
      rank: team.rank,
      challengeId: challenge.challengeId,
      challengeName: challenge.challengeName,
      instanceStatus: pick(['InstanceRunning', 'InstanceRunning', 'InstanceNotCreated', 'InstanceExpired']),
      breakStatus,
      fixStatus,
      serviceStatus,
      attackAttempts: randomInt(0, 5),
      defenseAttempts: randomInt(0, 3),
      currentRoundScore: breakStatus === 'BreakSuccess' || fixStatus === 'FixSuccess' ? randomInt(0, 180) : 0,
      lastActivityAt: new Date(now.getTime() - randomInt(20 + index, 1200) * 1000).toISOString(),
    } satisfies AwdpTeamChallengeState
  }))
}

function createMockTimeline(currentRound: number): AwdpRoundStat[] {
  const start = Math.max(1, currentRound - 13)
  return Array.from({ length: currentRound - start + 1 }, (_, offset) => {
    const round = start + offset
    return {
      round,
      attackSuccessCount: randomInt(4, 18),
      defenseSuccessCount: randomInt(3, 16),
      attackFailCount: randomInt(4, 22),
      defenseFailCount: randomInt(1, 13),
      scoreDelta: randomInt(120, 760),
      activeTeamCount: randomInt(5, 12),
    }
  })
}

function createInitialEvents(
  gameId: string,
  currentRound: number,
  teams: AwdpTeamScore[],
  challenges: AwdpChallengeStatus[],
  now: Date,
) {
  return Array.from({ length: 24 }, (_, index) => {
    const type = pick(eventTypes)
    const team = pick(teams)
    const challenge = pick(challenges)
    const event: AwdpScreenEvent = {
      id: `mock-initial-${index}`,
      gameId,
      round: currentRound,
      type,
      teamId: team.teamId,
      teamName: team.teamName,
      challengeId: challenge.challengeId,
      challengeName: challenge.challengeName,
      challengeCategory: challenge.category,
      level: levelFor(type),
      message: '',
      createdAt: new Date(now.getTime() - index * randomInt(14, 48) * 1000).toISOString(),
    }
    event.message = buildSafeEventMessage(event)
    return event
  })
}

function levelFor(type: AwdpScreenEventType): AwdpScreenEventLevel {
  if (type === 'ATTACK_ACCEPTED' || type === 'DEFENSE_CHECK_PASSED')
    return 'success'
  if (type === 'SERVICE_ERROR')
    return 'danger'
  if (type === 'ATTACK_REJECTED' || type === 'DEFENSE_CHECK_FAILED')
    return 'warning'
  return 'info'
}

function pick<T>(items: T[]) {
  return items[Math.floor(Math.random() * items.length)]
}

function randomInt(min: number, max: number) {
  return Math.floor(Math.random() * (max - min + 1)) + min
}
