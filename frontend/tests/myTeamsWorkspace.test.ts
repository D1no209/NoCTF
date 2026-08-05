import type { PublicCompetition } from '../src/api/competitionPresentation'
import type { PublicTeam } from '../src/api/teamPresentation'
import { describe, expect, test } from 'bun:test'
import { loadMyTeamsWorkspace } from '../src/components/teams/myTeamRegistrations'

const navBarSource = await Bun.file(new URL('../src/components/layout/NavBar.vue', import.meta.url)).text()
const routerSource = await Bun.file(new URL('../src/router/index.ts', import.meta.url)).text()
const workspaceSource = await Bun.file(new URL('../src/components/teams/MyTeamsWorkspace.vue', import.meta.url)).text()
const registrationWorkspaceSource = await Bun.file(new URL('../src/components/competition-registration/CompetitionRegistrationWorkspace.vue', import.meta.url)).text()

const competitions: PublicCompetition[] = [
  {
    id: 'competition-one',
    title: 'Competition one',
    description: null,
    mode: 'ctf',
    startTime: '2026-08-01T00:00:00Z',
    endTime: '2026-08-02T00:00:00Z',
    status: 'published',
    teamRegistrationAutoApprove: true,
    maxTeamMembers: 5,
    maxConcurrentRuntimeInstancesPerTeam: 1,
    ownerId: 'owner-one',
    leaderboardVisibility: 0,
  },
  {
    id: 'competition-two',
    title: 'Competition two',
    description: null,
    mode: 'awd',
    startTime: '2026-08-03T00:00:00Z',
    endTime: '2026-08-04T00:00:00Z',
    status: 'visible',
    teamRegistrationAutoApprove: false,
    maxTeamMembers: 4,
    maxConcurrentRuntimeInstancesPerTeam: 2,
    ownerId: 'owner-two',
    leaderboardVisibility: 0,
  },
]

const team: PublicTeam = {
  id: 'team-one',
  competitionId: competitions[0]!.id,
  name: 'Alpha',
  avatarUrl: null,
  captainId: 'captain-one',
  memberIds: ['captain-one'],
  memberCount: 1,
  registrationStatus: 'approved',
  isLocked: false,
  isBanned: false,
  registeredAt: '2026-08-01T00:00:00Z',
}

describe('my teams workspace', () => {
  test('separates current teams from competitions that can accept a new team', async () => {
    const requestedCompetitionIds: string[] = []

    const result = await loadMyTeamsWorkspace(
      async () => competitions,
      async (competitionId) => {
        requestedCompetitionIds.push(competitionId)
        return competitionId === competitions[0]!.id ? team : null
      },
      async () => null,
    )

    expect(requestedCompetitionIds).toEqual(competitions.map(competition => competition.id))
    expect(result).toEqual({
      registrations: [{ competition: competitions[0], team, banCase: null }],
      availableCompetitions: [competitions[1]],
    })
  })

  test('exposes the teams workspace from desktop and mobile navigation', () => {
    expect(routerSource).toContain('path: \'/teams\'')
    expect(routerSource).toContain('name: \'my-teams\'')
    expect(navBarSource.match(/to="\/teams"/g)).toHaveLength(2)
    expect(navBarSource).toContain('t(\'nav.teams\')')
  })

  test('presents teams as the workspace subject and competitions as context', () => {
    expect(workspaceSource).toContain(':key="item.team.id"')
    expect(workspaceSource).toMatch(/<h2[^>]*>\s*\{\{ item\.team\.name \}\}/)
    expect(workspaceSource).toContain('t(\'teams.competition\')')
    expect(workspaceSource).not.toContain('v-else class="flex flex-1 items-center p-4"')
  })

  test('owns team creation and editing instead of delegating creation to competition pages', () => {
    expect(workspaceSource).toContain('teamApi.create(')
    expect(workspaceSource).toContain('teamApi.update(')
    expect(registrationWorkspaceSource).not.toContain('teamApi.create(')
    expect(registrationWorkspaceSource).not.toContain('teamApi.join(')
    expect(registrationWorkspaceSource).toContain('to="/teams"')
  })
})
