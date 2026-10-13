import type { NoCtfapiEndpointsAdministrationGameplayFactsAwdCheckEvidenceResponse as Evidence, NoCtfapiEndpointsAdministrationGameplayFactsAwdCheckListItemResponse as Check } from '../app/api'
import { awdEvidenceCanBeReviewed } from '../app/features/admin/awd/awd-check-evidence'
import type { Data } from './schema'

/** Local review fixtures contain identifiers and probe outcomes only. */
export function createMockAwdChecks(state: { competitions: Data[]; challenges: Data[]; teams: Data[] }) {
  const records: { competitionId: string; item: Check; evidence: Evidence }[] = []
  for (const competition of state.competitions.filter(item => item.mode === 'Awd')) {
    const challenge = state.challenges.find(item => item.competitionId === competition.id)
    if (!challenge) continue
    for (const [index, team] of state.teams.filter(item => item.competitionId === competition.id).slice(0, 3).entries()) {
      const time = (seconds: number) => new Date(Date.now() - 300000 + seconds * 1000).toISOString()
      const passed = { outcome: 'Passed', reason: 'None' } as const
      const unknown = { outcome: 'Unknown', reason: 'None' } as const
      const item: Check = { gameplayFactId: crypto.randomUUID(), competitionChallengeId: challenge.id, teamId: team.id,
        roundId: crypto.randomUUID(), roundNumber: 4, scheduledAt: time(0), finalScheduledCheck: true,
        health: index === 2 ? 'Ok' : 'InternalError', execution: index === 2 ? 'Completed' : 'FailedToDetermine',
        settlement: index === 2 ? 'Final' : 'PendingAdjudication', reason: index === 0 ? 'LateReceipt' : index === 1 ? 'CallbackMissing' : 'None',
        receivedAt: index === 1 ? null : time(70) }
      const evidence: Evidence = { ...item, attemptId: crypto.randomUUID(), attempt: 2, attemptStartedAt: time(0), attemptDeadline: time(25),
        deadline: time(30), receiptDeadline: time(60), observedAt: index === 1 ? null : time(20), adjudicatedAt: null,
        knownTargetFailure: index === 0 ? 'Recovering' : null, connection: index === 1 ? unknown : passed, functionality: index === 1 ? unknown : passed,
        manifestComplete: true, evidenceComplete: index !== 1, requiredFlags: 2, readableFlags: index === 2 ? 2 : index === 0 ? 1 : 0,
        flags: [{ flagId: crypto.randomUUID(), historical: false, requiresWrite: true, obligation: index === 1 ? 'PlatformPending' : 'PlayerRequired', write: index === 1 ? unknown : passed, read: index === 1 ? unknown : passed },
          { flagId: crypto.randomUUID(), historical: true, requiresWrite: false, obligation: 'PlayerRequired', write: { outcome: 'NotRequired', reason: 'None' }, read: index === 0 ? { outcome: 'TargetFailure', reason: 'HistoricalFlagMissing' } : index === 1 ? unknown : passed }] }
      records.push({ competitionId: competition.id, item, evidence })
    }
  }
  const problem = (status: number, detail: string) => Response.json({ status, detail }, { status })
  return { handle(request: Request, route: string, p: Record<string, string>, body: Data): Response | undefined {
    const list = '/admin/competitions/{competitionId}/awd/checks'
    const detail = '/admin/competitions/{competitionId}/awd/checks/{gameplayFactId}'
    if (![list, detail, detail + '/adjudication'].includes(route)) return
    if (!state.competitions.some(item => item.id === p.competitionId && item.mode === 'Awd')) return problem(404, 'AWD competition not found')
    const url = new URL(request.url)
    if (request.method === 'GET' && route === list) {
      const states = ['Provisional', 'Final', 'PendingAdjudication', 'Exempted']
      const settlement = states[Number(url.searchParams.get('settlement') ?? 2)]
      const items = records.filter(record => record.competitionId === p.competitionId).map(record => record.item)
        .filter(item => (url.searchParams.get('allSettlements') === 'true' || item.settlement === settlement)
          && (!url.searchParams.get('teamId') || item.teamId === url.searchParams.get('teamId'))
          && (!url.searchParams.get('competitionChallengeId') || item.competitionChallengeId === url.searchParams.get('competitionChallengeId'))
          && (!url.searchParams.get('roundNumber') || item.roundNumber === Number(url.searchParams.get('roundNumber'))))
      const offset = Math.max(0, Number(url.searchParams.get('offset') ?? 0)), limit = Math.max(1, Number(url.searchParams.get('limit') ?? 10))
      return Response.json({ items: items.slice(offset, offset + limit), total: items.length })
    }
    const record = records.find(record => record.competitionId === p.competitionId && record.item.gameplayFactId === p.gameplayFactId)
    if (!record) return problem(404, 'AWD evidence not found')
    if (request.method === 'GET' && route === detail) return Response.json(record.evidence)
    if (request.method !== 'POST' || route !== detail + '/adjudication') return problem(405, 'Method not allowed')
    const evidence = record.evidence
    if (!body.reason?.trim() || body.reason.length > 512 || !['AcceptEvidence', 'Exempt'].includes(body.action)) return problem(400, 'Decision and reason are required')
    if (evidence.settlement !== 'PendingAdjudication' || body.expectedAttemptId !== evidence.attemptId || body.expectedReceivedAt !== evidence.receivedAt) return problem(409, 'Evidence changed; reload it before deciding')
    if (body.action === 'AcceptEvidence') {
      if (!awdEvidenceCanBeReviewed(evidence)) return problem(409, 'Evidence is incomplete')
      Object.assign(evidence, { health: 'Recovering', knownTargetFailure: 'Recovering', reason: 'HistoricalFlagMissing', settlement: 'Final', execution: 'Completed' })
    }
    else evidence.settlement = 'Exempted'
    evidence.adjudicatedAt = new Date().toISOString()
    Object.assign(record.item, { health: evidence.health, reason: evidence.reason, settlement: evidence.settlement, execution: evidence.execution })
    return Response.json(evidence)
  } }
}
