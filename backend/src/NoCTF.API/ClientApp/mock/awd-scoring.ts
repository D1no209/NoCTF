import { awdScoringPolicy, awdScoringValues } from '../app/features/admin/awd/awd-scoring-policy'
import type { NoCtfapiEndpointsAdministrationCompetitionsAwdScoringRuleAuditResponse as Audit, NoCtfapiEndpointsAdministrationCompetitionsAwdScoringPolicyContract as Policy } from '../app/api'
import type { Data } from './schema'

/** Demonstration score deltas only; production projection is exercised by backend integration tests. */
export function createMockAwdScoring(state: { competitions: Data[]; challenges: Data[]; teams: Data[] }) {
  const issued = awdScoringPolicy(awdScoringValues({}))
  const changes = new Map<string, Audit[]>()
  const tokens = new Map<string, { candidate: string; expires: number; revision: number }>()
  const policy = (challenge: string, round: number): Policy => changes.get(challenge)?.find(change => change.firstRound! <= round && change.lastRound! >= round)?.policy ?? issued
  const signature = (competition: string, challenge: string, body: Data) => JSON.stringify({ competition, challenge, firstRound: body.firstRound, lastRound: body.lastRound, timing: body.timing, reason: body.reason, policy: body.policy })
  const problem = (status: number, detail: string) => Response.json({ status, detail }, { status })
  return { publicAmendment(challenge: string, round: number) {
    const change = changes.get(challenge)?.find(item => item.firstRound! <= round && item.lastRound! >= round)
    return change ? { scoring: change.policy, scoringChangeEventId: change.eventId ?? null } : undefined
  }, handle(request: Request, route: string, p: Record<string, string>, body: Data, actor?: string): Response | undefined {
    const base = '/admin/competitions/{competitionId}/challenges/{competitionChallengeId}/awd/'
    if (![base + 'scoring-rules', base + 'scoring-preview', base + 'scoring'].includes(route)) return
    const challenge = p.competitionChallengeId!, competition = p.competitionId!
    if (!state.competitions.some(item => item.id === competition && item.mode === 'Awd') || !state.challenges.some(item => item.id === challenge && item.competitionId === competition)) return problem(404, 'AWD challenge not found')
    const audit = changes.get(challenge) ?? []
    const url = new URL(request.url)
    if (request.method === 'GET') {
      const round = Number(url.searchParams.get('roundNumber') ?? 4)
      if (round < 1 || round > 4) return problem(404, 'No issued rules for this round')
      return Response.json({ roundNumber: round, roundId: `mock-round-${round}`, policies: [{ rulesId: `mock-rules-${round}`, revision: 1, enabledAt: new Date(Date.now() - 180000).toISOString(),
        issuedPolicy: issued, effectivePolicy: policy(challenge, round), appliedChangeEventId: audit.find(change => change.firstRound! <= round && change.lastRound! >= round)?.eventId ?? null }], recentChanges: audit.slice(0, 50) })
    }
    if (request.method !== 'POST') return problem(405, 'Method not allowed')
    if (!body.reason?.trim() || !body.policy || body.firstRound < 1 || body.lastRound > 4 || body.firstRound > body.lastRound
      || !['Immediately', 'RecalculateHistory'].includes(body.timing) || (body.timing === 'Immediately' && (body.firstRound !== 4 || body.lastRound !== 4))) return problem(422, 'Invalid scoring change')
    if (route.endsWith('scoring-preview')) {
      const token = crypto.randomUUID(), expires = Date.now() + 120000
      tokens.set(token, { candidate: signature(competition, challenge, body), expires, revision: audit.length })
      const teams = state.teams.filter(team => team.competitionId === competition)
      return Response.json({ token, projectedAt: new Date().toISOString(), expiresAt: new Date(expires).toISOString(), affectedFlags: teams.length * 2 * (body.lastRound - body.firstRound + 1), affectedRuleSnapshots: body.lastRound - body.firstRound + 1,
        teams: teams.map((team, index) => ({ teamId: team.id, teamName: team.name, scoreBefore: 1000 + index * 100,
          scoreAfter: 1000 + index * 100 + (body.policy.attackPoints - (policy(challenge, body.firstRound).attackPoints ?? 50)) * (index + 1) })) })
    }
    const preview = tokens.get(body.previewToken)
    if (!preview || preview.expires <= Date.now() || preview.revision !== audit.length || preview.candidate !== signature(competition, challenge, body)) return problem(409, 'Preview expired or facts changed')
    tokens.delete(body.previewToken)
    const eventId = crypto.randomUUID()
    changes.set(challenge, [{ eventId, actorUserId: actor, reason: body.reason, firstRound: body.firstRound, lastRound: body.lastRound, timing: body.timing, appliedAt: new Date().toISOString(), policy: body.policy }, ...audit])
    return Response.json({ eventId })
  } }
}
