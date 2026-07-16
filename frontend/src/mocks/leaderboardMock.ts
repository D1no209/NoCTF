
// Dev-only mock generator for leaderboard data. This file is dynamically imported
// by src/mocks/runtime.ts only when import.meta.env.DEV is true, so it never ships
// to production.

export interface MockTrendPoint {
  timestamp: string
  score: number
}

export interface MockTrendSeries {
  teamId: string
  teamName: string
  points: MockTrendPoint[]
}

export interface MockLeaderboardEntry {
  rank?: number
  teamId?: string
  teamName?: string
  trackName?: string | null
  totalScore?: number
  score?: number
  solvedCount?: number
  firstSolveAt?: string | null
}

const prefixes = [
  'Red', 'Blue', 'Null', 'Void', 'Cyber', 'Byte', 'Neo', 'Shadow',
  'Ghost', 'Phantom', 'Iron', 'Steel', 'Quantum', 'Atomic', 'Solar',
  'Lunar', 'Hyper', 'Meta', 'Data', 'Code', 'Bit', 'Chip', 'Net',
  'Web', 'Pwn', 'Root', 'Shell', 'Hex', 'Pixel', 'Core',
]

const suffixes = [
  'Team', 'Crew', 'Squad', 'Unit', 'Division', 'Alliance', 'Guild',
  'Collective', 'Syndicate', 'Labs', 'Ops', 'Force', 'Brigade',
  'Phalanx', 'Circle', 'Order', 'Faction', 'Clan', 'Hive', 'Node',
]

function mulberry32(seed: number) {
  return function () {
    let t = seed += 0x6D2B79F5
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

function parseSeed(value: string) {
  let hash = 0
  for (let i = 0; i < value.length; i++) {
    hash = (hash << 5) - hash + value.charCodeAt(i)
    hash |= 0
  }
  return hash
}

function formatTimestamp(time: number) {
  return new Date(time).toISOString()
}

export function generateLeaderboardEntries(
  competitionId: string,
  count = 100,
): { entries: MockLeaderboardEntry[] } {
  const rng = mulberry32(parseSeed(competitionId))
  const tracks = competitionId.includes('awdp') ? ['Open', 'Campus'] : []
  const startTime = Date.now() - 2 * 60 * 60 * 1000

  const entries: MockLeaderboardEntry[] = Array.from({ length: count }, (_, index) => {
    const teamNumber = index + 1
    const prefix = prefixes[Math.floor(rng() * prefixes.length)]
    const suffix = suffixes[Math.floor(rng() * suffixes.length)]
    const totalScore = Math.floor(rng() * 5000)
    const solvedCount = Math.max(0, Math.floor(totalScore / 300) + Math.floor(rng() * 6) - 2)
    const firstSolveOffset = Math.floor(rng() * 2 * 60 * 60 * 1000)

    return {
      rank: 0,
      teamId: `team-mock-${teamNumber}`,
      teamName: `${prefix} ${suffix} ${teamNumber}`,
      trackName: tracks.length ? tracks[Math.floor(rng() * tracks.length)] : null,
      totalScore,
      solvedCount,
      firstSolveAt: formatTimestamp(startTime + firstSolveOffset),
    }
  })

  entries.sort((a, b) => (b.totalScore ?? 0) - (a.totalScore ?? 0))
  entries.forEach((entry, index) => {
    entry.rank = index + 1
  })

  return { entries }
}

export function generateLeaderboardTrend(
  competitionId: string,
  rounds = 24,
): { series: MockTrendSeries[] } {
  const { entries } = generateLeaderboardEntries(competitionId, 100)
  const topTeams = entries.slice(0, 10)
  const now = Date.now()
  const interval = 5 * 60 * 1000
  const startTime = now - rounds * interval

  const series: MockTrendSeries[] = topTeams.map((entry) => {
    const targetScore = entry.totalScore ?? 0
    const teamSeed = parseSeed(entry.teamId ?? '')
    const rng = mulberry32(teamSeed)
    const points: MockTrendPoint[] = []
    let currentScore = 0

    for (let round = 0; round <= rounds; round++) {
      const progress = round / rounds
      const noise = (rng() - 0.5) * 200
      currentScore = Math.max(0, Math.floor(targetScore * progress + noise))
      points.push({
        timestamp: formatTimestamp(startTime + round * interval),
        score: currentScore,
      })
    }

    points[points.length - 1].score = targetScore

    return {
      teamId: entry.teamId ?? '',
      teamName: entry.teamName ?? '',
      points,
    }
  })

  return { series }
}
