import type { NoCtfapiEndpointsLiveSoloLiveSoloBracketResponse as Bracket, NoCtfapiEndpointsTeamsTeamResponse as Team,
  NoCtfapiEndpointsLiveSoloLiveSoloBracketMatchResponse as Row } from '../../api'
import type { MessageKey } from '../../locales/en'
import { matchStateKey } from './live-solo-state'
const lanes = { Winners: 'liveSolo.settings.winners', Losers: 'liveSolo.settings.losers', GrandFinal: 'liveSolo.settings.grandFinal', ResetFinal: 'liveSolo.settings.resetFinal' } as const
export function hasSeededBracket(bracket: Bracket | null) { return bracket?.matches?.some(row => row.sources?.some(source => source.seed != null || source.sourceMatchId != null)) ?? false }
export function selectableTeams(teams: Team[], bracket: Bracket | null): Team[] {
  const active = new Set((bracket?.matches ?? []).filter(row => !['Completed', 'Canceled'].includes(row.match?.state ?? ''))
    .flatMap(row => [row.match?.leftTeamId, row.match?.rightTeamId]).filter(Boolean))
  return teams.filter(team => team.id && team.registrationStatus === 'Approved' && !team.isBanned && !active.has(team.id))
}
export function bracketRows(bracket: Bracket | null): { id: string; row: Row; number: number; stateKey: MessageKey; laneKey: MessageKey }[] {
  return (bracket?.matches ?? []).filter(row => row.match?.id).map((row, index) => ({ id: row.match!.id!, row, number: index + 1,
    stateKey: matchStateKey(row.match?.state), laneKey: lanes[row.lane ?? 'Winners'] }))
}
export function reorderSeed(ids: string[], index: number, direction: -1 | 1): string[] {
  const target = index + direction; if (target < 0 || target >= ids.length) return ids
  const next = [...ids]; [next[index], next[target]] = [next[target]!, next[index]!]; return next
}
