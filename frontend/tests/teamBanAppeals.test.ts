import { describe, expect, test } from 'bun:test'

const apiSource = await Bun.file(
  new URL('../src/api/teamBanAppealApi.ts', import.meta.url),
).text()
const generatedSource = await Bun.file(
  new URL('../src/api/generated/sdk.gen.ts', import.meta.url),
).text()
const adminWorkspaceSource = await Bun.file(
  new URL(
    '../src/components/admin/competition-detail/AdminCompetitionTeamBanAppealsPanel.vue',
    import.meta.url,
  ),
).text()
const teamWorkspaceSource = await Bun.file(
  new URL('../src/components/teams/MyTeamsWorkspace.vue', import.meta.url),
).text()

describe('private team ban appeals', () => {
  test('uses generated operations without manual API paths', () => {
    for (const operation of [
      'getMyTeamBanCase',
      'submitTeamBanAppeal',
      'adminListTeamBanAppeals',
      'adminAcceptTeamBanAppeal',
      'adminUpholdTeamBanAppeal',
      'adminCorrectTeamBan',
    ]) {
      expect(apiSource).toContain(operation)
      expect(generatedSource).toContain(`export const ${operation}`)
    }
    expect(apiSource).not.toContain('/api/')
    expect(apiSource).not.toMatch(/https?:\/\//)
  })

  test('keeps participant appeals private and staff decisions explicit', () => {
    expect(teamWorkspaceSource).toContain('item.banCase.canAppeal')
    expect(teamWorkspaceSource).toContain('teamBanAppealApi.submit(')
    expect(teamWorkspaceSource).toContain('teams.banAppeal.privateDescription')
    expect(adminWorkspaceSource).toContain('openResolution(banCase, \'accept\')')
    expect(adminWorkspaceSource).toContain('openResolution(banCase, \'uphold\')')
    expect(adminWorkspaceSource).toContain('banCase.canResolve')
  })
})
