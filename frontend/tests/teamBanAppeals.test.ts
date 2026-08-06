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
const registrationWorkspaceSource = await Bun.file(
  new URL('../src/components/competition-registration/CompetitionRegistrationWorkspace.vue', import.meta.url),
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
    expect(registrationWorkspaceSource).toContain('banCase.canAppeal')
    expect(registrationWorkspaceSource).toContain('teamBanAppealApi.submit(')
    expect(registrationWorkspaceSource).toContain('teams.banAppeal.privateDescription')
    expect(adminWorkspaceSource).toContain('openResolution(banCase, \'accept\')')
    expect(adminWorkspaceSource).toContain('openResolution(banCase, \'uphold\')')
    expect(adminWorkspaceSource).toContain('banCase.canResolve')
  })
})
