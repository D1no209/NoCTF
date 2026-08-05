import type { PublicCompetition } from '@/api/competitionPresentation'
import type { MyTeamBanCase } from '@/api/teamBanAppealApi'
import type { PublicTeam } from '@/api/teamPresentation'
import { competitionApi, teamApi } from '@/api/noctf'
import { teamBanAppealApi } from '@/api/teamBanAppealApi'

export interface MyTeamRegistration {
  competition: PublicCompetition
  team: PublicTeam
  banCase: MyTeamBanCase | null
}

export interface MyTeamsWorkspaceData {
  registrations: MyTeamRegistration[]
  availableCompetitions: PublicCompetition[]
}

type LoadCompetitions = () => Promise<PublicCompetition[]>
type LoadTeam = (competitionId: string) => Promise<PublicTeam | null>
type LoadBanCase = (competitionId: string) => Promise<MyTeamBanCase | null>

export async function loadMyTeamsWorkspace(
  loadCompetitions: LoadCompetitions = competitionApi.list,
  loadTeam: LoadTeam = teamApi.getMy,
  loadBanCase: LoadBanCase = teamBanAppealApi.getMy,
): Promise<MyTeamsWorkspaceData> {
  const competitions = await loadCompetitions()
  const memberships = await Promise.all(competitions.map(async (competition) => {
    const team = await loadTeam(competition.id)
    const banCase = team ? await loadBanCase(competition.id) : null
    return { competition, team, banCase }
  }))

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
