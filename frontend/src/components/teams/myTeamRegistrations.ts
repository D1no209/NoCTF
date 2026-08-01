import type { PublicCompetition } from '@/api/competitionPresentation'
import type { PublicTeam } from '@/api/teamPresentation'
import { competitionApi, teamApi } from '@/api/noctf'

export interface MyTeamRegistration {
  competition: PublicCompetition
  team: PublicTeam
}

type LoadCompetitions = () => Promise<PublicCompetition[]>
type LoadTeam = (competitionId: string) => Promise<PublicTeam | null>

export async function loadMyTeamRegistrations(
  loadCompetitions: LoadCompetitions = competitionApi.list,
  loadTeam: LoadTeam = teamApi.getMy,
): Promise<MyTeamRegistration[]> {
  const competitions = await loadCompetitions()
  const registrations = await Promise.all(competitions.map(async competition => ({
    competition,
    team: await loadTeam(competition.id),
  })))

  return registrations.filter(
    (registration): registration is MyTeamRegistration => registration.team !== null,
  )
}
