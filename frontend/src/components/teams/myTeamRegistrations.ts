import type { PublicCompetition } from '@/api/competitionPresentation'
import type { PublicTeam } from '@/api/teamPresentation'
import { competitionApi, teamApi } from '@/api/noctf'

export interface MyTeamRegistration {
  competition: PublicCompetition
  team: PublicTeam
}

export interface MyTeamsWorkspaceData {
  registrations: MyTeamRegistration[]
  availableCompetitions: PublicCompetition[]
}

type LoadCompetitions = () => Promise<PublicCompetition[]>
type LoadTeam = (competitionId: string) => Promise<PublicTeam | null>

export async function loadMyTeamsWorkspace(
  loadCompetitions: LoadCompetitions = competitionApi.list,
  loadTeam: LoadTeam = teamApi.getMy,
): Promise<MyTeamsWorkspaceData> {
  const competitions = await loadCompetitions()
  const memberships = await Promise.all(competitions.map(async competition => ({
    competition,
    team: await loadTeam(competition.id),
  })))

  const registrations = memberships.filter(
    (registration): registration is MyTeamRegistration => registration.team !== null,
  )
  const unavailableStatuses = new Set<PublicCompetition['status']>([
    'running',
    'paused',
    'finished',
  ])

  return {
    registrations,
    availableCompetitions: memberships
      .filter(membership =>
        membership.team === null && !unavailableStatuses.has(membership.competition.status),
      )
      .map(membership => membership.competition),
  }
}
