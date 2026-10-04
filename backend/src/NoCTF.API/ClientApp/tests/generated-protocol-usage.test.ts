import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('generated protocol usage', () => {
  test('does not mirror the generated account role as a runtime enum', async () => {
    const source = await sourceFile(new URL('../app/composables/useAuth.ts', import.meta.url)).text()

    expect(source).not.toContain('export const UserRole')
    expect(source).toContain("user.value?.role === 'Administrator'")
    expect(source).toContain("user.value?.role === 'Organizer'")
  })

  test('does not mirror generated competition, team, runtime, or gameplay enums', async () => {
    const labels = await sourceFile(new URL('../app/utils/labels.ts', import.meta.url)).text()
    const gameConfig = await sourceFile(new URL('../app/utils/game-config.ts', import.meta.url)).text()

    for (const name of [
      'GameMode',
      'CompetitionStatus',
      'TeamRegistrationStatus',
      'RuntimeState',
      'GameplayFactKind',
      'GameplayFactState',
      'GameplayFactResult',
      'LeaderboardDataScope',
    ]) {
      expect(labels).not.toContain(`export const ${name}`)
    }
    expect(gameConfig).toContain(
      'export type GameModeValue = NoCTFAPIEndpointsCompetitionsGameModeProtocol',
    )
  })

  test('uses the generated competition administration role', async () => {
    const source = await sourceFile(new URL('../app/lib/admin-competition.ts', import.meta.url)).text()

    expect(source).toContain(
      'export type CompetitionAdminRole = NoCTFAPIEndpointsCompetitionsCompetitionAdministrationRoleProtocol',
    )
    expect(source).not.toContain("'owner' | 'manager' | 'judge' | 'observer'")
  })

  test('constrains admin label maps to generated protocol unions', async () => {
    const source = await sourceFile(new URL('../app/utils/admin-format.ts', import.meta.url)).text()

    expect(source).not.toContain('Record<string, string> =')
    expect(source).toContain(
      'satisfies Record<NoCTFAPIEndpointsRuntimeRuntimeStateProtocol, string>',
    )
    expect(source).toContain(
      'satisfies Record<NoCTFAPIEndpointsGameplayFactsGameplayFactResultProtocol, string>',
    )
  })

  test('uses generated protocol types in the strongly typed hub contract', async () => {
    const hub = await sourceFile(new URL('../app/composables/useCompetitionHub.ts', import.meta.url)).text()
    const flagSubmit = await sourceFile(new URL('../app/features/challenges/FlagSubmit.vue', import.meta.url)).text()
    const fixSubmit = await sourceFile(new URL('../app/features/challenges/FixSubmit.vue', import.meta.url)).text()

    expect(hub).toContain("competitionHubString(payload, 'competitionId')")
    expect(hub).toContain('export interface CompetitionHubClientEvents')
    expect(hub).toContain('NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse')
    expect(hub).not.toContain('gameplayFactStateChanged?: (payload: unknown)')
    expect(flagSubmit).toContain("competitionHubString(payload, 'gameplayFactId')")
    expect(fixSubmit).toContain('NoCTFAPIEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol')
    expect(fixSubmit).toContain('NoCTFAPIEndpointsGameplayFactsUploadPatchFailureCodeProtocol')
    expect(`${hub}\n${flagSubmit}\n${fixSubmit}`).not.toContain('as { gameplayFactId?: unknown }')
  })
})
