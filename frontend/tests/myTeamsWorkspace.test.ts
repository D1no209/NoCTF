import { describe, expect, test } from 'bun:test'
import { toGlobalTeam } from '../src/api/globalTeamPresentation'

const navBarSource = await Bun.file(
  new URL('../src/components/layout/NavBar.vue', import.meta.url),
).text()
const routerSource = await Bun.file(
  new URL('../src/router/index.ts', import.meta.url),
).text()
const workspaceSource = await Bun.file(
  new URL('../src/components/teams/MyTeamsWorkspace.vue', import.meta.url),
).text()
const registrationWorkspaceSource = await Bun.file(
  new URL(
    '../src/components/competition-registration/CompetitionRegistrationWorkspace.vue',
    import.meta.url,
  ),
).text()
const teamApiSource = await Bun.file(
  new URL('../src/api/noctf.ts', import.meta.url),
).text()
const en = await Bun.file(new URL('../src/locales/en.json', import.meta.url)).json()
const zh = await Bun.file(new URL('../src/locales/zh-CN.json', import.meta.url)).json()

describe('global team workspace', () => {
  test('normalizes the generated global team response', () => {
    expect(toGlobalTeam({
      id: 'team-one',
      name: 'Alpha',
      avatarUrl: null,
      captainId: 'captain-one',
      memberIds: ['captain-one', 'member-two'],
      invitationToken: '0123456789abcdef0123456789abcdef',
      createdAt: '2026-08-06T00:00:00Z',
    })).toEqual({
      id: 'team-one',
      name: 'Alpha',
      avatarUrl: null,
      captainId: 'captain-one',
      memberIds: ['captain-one', 'member-two'],
      memberCount: 2,
      invitationToken: '0123456789abcdef0123456789abcdef',
      createdAt: '2026-08-06T00:00:00Z',
    })
  })

  test('exposes the teams workspace from desktop and mobile navigation', () => {
    expect(routerSource).toContain('path: \'/teams\'')
    expect(routerSource).toContain('name: \'my-teams\'')
    expect(navBarSource.match(/to="\/teams"/g)).toHaveLength(2)
    expect(navBarSource).toContain('t(\'nav.teams\')')
    expect(routerSource).toContain(
      'savedPosition ?? { left: 0, top: 0 }',
    )
  })

  test('creates and joins global teams without a competition selector', () => {
    expect(workspaceSource).toContain('queryFn: teamApi.listMine')
    expect(workspaceSource).toContain('teamApi.create({')
    expect(workspaceSource).toContain('teamApi.join(invitationToken.value.trim())')
    expect(workspaceSource).not.toContain('selectedCompetitionId')
    expect(workspaceSource).not.toContain('teams.noAvailableCompetitions')
    expect(workspaceSource).not.toContain('loadMyTeamsWorkspace')
    expect(teamApiSource).toContain('noCtfapiEndpointsTeamsListMyTeamsEndpoint')
    expect(teamApiSource).toContain('noCtfapiEndpointsTeamsCreateTeamEndpoint')
  })

  test('keeps competition registration separate and snapshots an existing team', () => {
    expect(registrationWorkspaceSource).toContain('teamApi.register(')
    expect(registrationWorkspaceSource).toContain('teamApi.listMine')
    expect(registrationWorkspaceSource).not.toContain('teamApi.create(')
    expect(registrationWorkspaceSource).not.toContain('teamApi.join(')
    expect(teamApiSource).toContain(
      'noCtfapiEndpointsTeamsRegisterTeamForCompetitionEndpoint',
    )
  })

  test('keeps the empty state informational with actions only in the page header', () => {
    expect(workspaceSource).toContain('t(\'teams.noTeamsUseHeader\')')
    const emptyState = workspaceSource.slice(
      workspaceSource.indexOf('v-if="!teamList.length"'),
      workspaceSource.indexOf('<div v-else class="grid gap-4 lg:grid-cols-2">'),
    )
    expect(emptyState).not.toContain('<Button')
  })

  test('localizes every visible invitation action', () => {
    expect(workspaceSource).toContain('t(\'common.copy\')')
    expect(en.common.copy).toBe('Copy')
    expect(zh.common.copy).toBe('复制')
  })
})
