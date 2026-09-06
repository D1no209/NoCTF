import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../api'

type Competition = Pick<NoCtfapiEndpointsCompetitionsCompetitionResponse, 'mode' | 'status' | 'practiceModeEnabled'>
type Team = Pick<NoCtfapiEndpointsTeamsTeamResponse, 'registrationStatus' | 'isBanned'>

export function isCtfPracticeOpen(competition: Competition | null | undefined): boolean {
  return competition?.mode === 'Ctf'
    && competition.status === 'Finished'
    && competition.practiceModeEnabled === true
}

export function canEnterCompetition(competition: Competition | null | undefined, team: Team | null | undefined): boolean {
  return team?.registrationStatus === 'Approved' && !team.isBanned
    && (competition?.status === 'Running' || isCtfPracticeOpen(competition))
}
