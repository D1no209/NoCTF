import type { NoCtfapiEndpointsCompetitionsSpectatorScreenResponse as Screen } from '../app/api'

const started = Date.now()
const id = (type: number, index: number) => `${type.toString().padStart(8, '0')}-0000-4000-8000-${index.toString().padStart(12, '0')}`
let scope: 'Live' | 'Frozen' | 'Hidden' = 'Live'
let frozenAt = 0
let pointCount = 3
let awdTeams = 32
let awdChallenges = 8
export function spectatorMock(request: Request): Response | null {
  const url = new URL(request.url)
  if (url.pathname === '/__mock/spectator' && request.method === 'POST') {
    const next = url.searchParams.get('scope')
    if (next === 'Live' || next === 'Hidden' || next === 'Frozen') { scope = next; frozenAt = Date.now() }
    const points = Number(url.searchParams.get('points')); if ([3, 12].includes(points)) pointCount = points
    const teams = Number(url.searchParams.get('teams')); if ([2, 12, 32, 64].includes(teams)) awdTeams = teams
    const questions = Number(url.searchParams.get('challenges')); if ([1, 3, 8].includes(questions)) awdChallenges = questions
    return Response.json({ scope, pointCount, awdTeams, awdChallenges })
  }
  const match = url.pathname.match(/\/competitions\/[^/]+\/(awd|koh)-spectator$/)
  if (!match) return null
  const mode = match[1] === 'awd' ? 'Awd' : 'Koh'
  const cursorTime = Number(url.searchParams.get('cursor')?.split(':')[0])
  const now = scope === 'Frozen' ? frozenAt : cursorTime || Date.now(), elapsed = Math.floor((now - started) / 1000)
  const teamCount = mode === 'Awd' ? awdTeams : 8, challengeCount = mode === 'Awd' ? awdChallenges : pointCount
  const teams = Array.from({ length: teamCount }, (_, i) => ({ id: id(91, i + 1), name: ['天枢', 'Aurora', 'Red Packet', '星河', 'Null Pointer', '破晓', 'Zero Day', '山海'][i % 8] + ` / ${String(i + 1).padStart(2, '0')}`, rank: i + 1, score: 9850 - i * 113 }))
  const challenges = Array.from({ length: challengeCount }, (_, i) => ({ competitionChallengeId: id(92, i + 1), title: (mode === 'Awd' ? ['Gateway', 'Vault', 'Archive', 'Kernel', 'Relay', 'Portal', 'Cache', 'Console'][i] : ['霜脊', '孤峰', '云台', '苍岩', '北岭', '流岚', '龙脊', '天隘', '白石', '雪峰', '远山', '青岚'][i])!, direction: 'Web', category: 'Web', order: i, isPublished: true }))
  const rate = mode === 'Awd' ? 20 : 1
  const total = Math.max(1, elapsed * rate), minutes = Number(url.searchParams.get('minutes') ?? 10)
  const count = Math.min(total, 2000, minutes * 60 * rate)
  const limit = Math.min(2000, Math.max(1, Number(url.searchParams.get('limit') ?? 256)))
  const offset = Number(url.searchParams.get('cursor')?.split(':')[1] ?? 0)
  const controlAt = (n: number) => n <= 0 || n % 7 === 0 ? null : teams[Math.floor(n / challengeCount) % teams.length]!.id
  const changes = Array.from({ length: Math.min(limit, Math.max(0, count - offset)) }, (_, i) => {
    const n = total - offset - i, source = n % teamCount, target = (source + 1 + n % (teamCount - 1)) % teamCount
    return { id: id(93, n), revision: id(94, n), at: new Date(started + n / rate * 1000).toISOString(), kind: mode === 'Awd' ? 'Attack' as const : n % 11 === 0 ? 'DetectionFailed' as const : n % 7 === 0 ? 'Uncontrolled' as const : 'Control' as const, challengeId: challenges[n % challengeCount]!.competitionChallengeId, teamId: teams[source]!.id, targetTeamId: mode === 'Awd' ? teams[target]!.id : null, controllerId: mode === 'Koh' ? controlAt(n % 11 === 0 ? n - challengeCount : n) : null }
  })
  const compromised = new Set<string>()
  if (mode === 'Awd') {
    // This-round facts are independent of the requested event page size.
    for (let n = Math.max(1, Math.ceil((now - 40000 - started) / 1000 * rate)); n <= total; n++) {
      const source = n % teamCount, target = (source + 1 + n % (teamCount - 1)) % teamCount
      compromised.add(`${teams[target]!.id}:${challenges[n % challengeCount]!.competitionChallengeId}`)
    }
  }
  const data: Screen = { version: String(now), serverTime: new Date().toISOString(), dataAsOf: new Date(now).toISOString(), scope, mode,
    competitionName: mode === 'Awd' ? 'NoCTF · 攻防对阵场 / MOCK' : 'NoCTF · 山巅争夺 / MOCK', status: 'Running', teams, challenges,
    round: mode === 'Awd' ? { id: id(95, 24), number: 24, startAt: new Date(now - 40000).toISOString(), endAt: new Date(now + 260000).toISOString() } : null,
    services: mode === 'Awd' ? teams.flatMap((team, i) => challenges.map((c, j) => ({ teamId: team.id, challengeId: c.competitionChallengeId, health: (i + j) % 13 === 0 ? 'Down' as const : (i + j) % 19 === 0 ? 'Unknown' as const : 'Healthy' as const, isCompromised: compromised.has(`${team.id}:${c.competitionChallengeId}`), checkedAt: new Date(now - 15000).toISOString() }))) : [],
    points: mode === 'Koh' ? challenges.map((c, i) => {
      const n = total - ((total - i + challengeCount) % challengeCount), unknown = n <= 0 || n % 11 === 0
      return { challengeId: c.competitionChallengeId, controllerId: controlAt(unknown ? n - challengeCount : n),
        observedTeamId: n > 0 ? teams[n % teamCount]!.id : null, health: unknown ? 'Unknown' : 'Healthy',
        checkedAt: n > 0 ? new Date(started + n * 1000).toISOString() : null, intervalSeconds: challengeCount, pointsPerInterval: 10 }
    }) : [],
    changes, settlements: mode === 'Awd' ? [23, 22, 21].flatMap(number => teams.map(team => ({ roundId: id(95, number), number, teamId: team.id, attack: 200, lost: 50, service: 800, other: 0 }))) : [],
    historyStartsAt: new Date(now - minutes * 60000).toISOString(), historyTruncated: total > 2000, nextCursor: offset + limit < count ? `${now}:${offset + limit}` : null }
  if (scope === 'Hidden') Object.assign(data, { teams: [], challenges: [], services: [], points: [], changes: [], settlements: [], round: null, nextCursor: null, historyTruncated: false, historyStartsAt: null })
  return Response.json(data)
}
