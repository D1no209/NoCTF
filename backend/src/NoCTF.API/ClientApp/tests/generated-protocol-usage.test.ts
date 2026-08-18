import { describe, expect, test } from 'bun:test'

describe('generated protocol usage', () => {
  test('does not mirror the generated account role as a runtime enum', async () => {
    const source = await Bun.file(new URL('../app/composables/useAuth.ts', import.meta.url)).text()

    expect(source).not.toContain('export const UserRole')
    expect(source).toContain("user.value?.role === 'Administrator'")
    expect(source).toContain("user.value?.role === 'Organizer'")
  })

  test('does not mirror generated competition, team, runtime, or gameplay enums', async () => {
    const labels = await Bun.file(new URL('../app/utils/labels.ts', import.meta.url)).text()
    const gameConfig = await Bun.file(new URL('../app/utils/game-config.ts', import.meta.url)).text()

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
      'export type GameModeValue = NoCtfapiEndpointsCompetitionsGameModeProtocol',
    )
  })

  test('uses the generated competition administration role', async () => {
    const source = await Bun.file(new URL('../app/lib/admin-competition.ts', import.meta.url)).text()

    expect(source).toContain(
      'export type CompetitionAdminRole = NoCtfapiEndpointsCompetitionsCompetitionAdministrationRoleProtocol',
    )
    expect(source).not.toContain("'owner' | 'manager' | 'judge' | 'observer'")
  })

  test('constrains admin label maps to generated protocol unions', async () => {
    const source = await Bun.file(new URL('../app/utils/admin-format.ts', import.meta.url)).text()

    expect(source).not.toContain('Record<string, string> =')
    expect(source).toContain(
      'satisfies Record<NoCtfapiEndpointsRuntimeRuntimeStateProtocol, string>',
    )
    expect(source).toContain(
      'satisfies Record<NoCtfapiEndpointsGameplayFactsGameplayFactResultProtocol, string>',
    )
  })

  test('narrows untyped hub payloads without declaring transport DTOs', async () => {
    const hub = await Bun.file(new URL('../app/composables/useCompetitionHub.ts', import.meta.url)).text()
    const flagSubmit = await Bun.file(new URL('../app/components/challenges/FlagSubmit.vue', import.meta.url)).text()
    const fixSubmit = await Bun.file(new URL('../app/components/challenges/FixSubmit.vue', import.meta.url)).text()

    expect(hub).toContain("competitionHubString(payload, 'competitionId')")
    expect(flagSubmit).toContain("competitionHubString(payload, 'gameplayFactId')")
    expect(fixSubmit).toContain('NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol')
    expect(fixSubmit).toContain('NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol')
    expect(`${hub}\n${flagSubmit}\n${fixSubmit}`).not.toContain('as { gameplayFactId?: unknown }')
  })
})
