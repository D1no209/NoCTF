import type { NoCTFAPIEndpointsCompetitionsCompetitionResponse, NoCTFAPIEndpointsTeamsTeamResponse } from '../api/models'

type Competition = Pick<NoCTFAPIEndpointsCompetitionsCompetitionResponse, 'mode' | 'status' | 'practiceModeEnabled' | 'allowTeamRegistrationWhileRunning'>
type Team = Pick<NoCTFAPIEndpointsTeamsTeamResponse, 'registrationStatus' | 'isBanned'>

export function isCtfPracticeOpen(competition: Competition | null | undefined): boolean {
  return competition?.mode === 'Ctf'
    && competition.status === 'Finished'
    && competition.practiceModeEnabled === true
}

export function canEnterCompetition(competition: Competition | null | undefined, team: Team | null | undefined): boolean {
  return team?.registrationStatus === 'Approved' && !team.isBanned
    && (competition?.status === 'Running' || isCtfPracticeOpen(competition))
}

export function canRegisterForCompetition(competition: Competition | null | undefined): boolean {
  return isCtfPracticeOpen(competition) || competition?.status === 'Visible' || competition?.status === 'Published'
    || competition?.status === 'Running' && competition.allowTeamRegistrationWhileRunning === true
}
