import type { NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../api'

type Competition = Pick<NoCtfapiEndpointsCompetitionsCompetitionResponse, 'mode' | 'status' | 'practiceModeEnabled' | 'allowTeamRegistrationWhileRunning'>
type Team = Pick<NoCtfapiEndpointsTeamsTeamResponse, 'registrationStatus' | 'isBanned'>

export function isCtfPracticeOpen(competition: Competition | null | undefined): boolean {
  return competition?.mode === 'Ctf'
    && competition.status === 'Finished'
    && competition.practiceModeEnabled === true
}

export function canEnterCompetition(competition: Competition | null | undefined, team: Team | null | undefined): boolean {
  return team?.registrationStatus === 'Approved' && !team.isBanned
    && (competition?.status === 'Running' || isCtfPracticeOpen(competition)
      || competition?.mode === 'LiveSolo' && ['Visible', 'Published', 'Paused', 'Finished'].includes(competition.status ?? ''))
}

export function canRegisterForCompetition(competition: Competition | null | undefined): boolean {
  return isCtfPracticeOpen(competition) || competition?.status === 'Visible' || competition?.status === 'Published'
    || competition?.status === 'Running' && competition.allowTeamRegistrationWhileRunning === true
}
