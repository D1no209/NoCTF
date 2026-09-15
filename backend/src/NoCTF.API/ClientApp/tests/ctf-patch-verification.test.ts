import { describe, expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

describe('CTF Patch Verification experiment', () => {
  test('keeps completion kind separate from challenge direction and serializes schema v3', async () => {
    const source = await sourceFile(new URL('../app/utils/game-config.ts', import.meta.url)).text()
    const runtime = await sourceFile(new URL('../app/features/admin/useDefinitionRuntime.ts', import.meta.url)).text()

    expect(source).toContain('DEFINITION_SCHEMA_VERSION = { Ctf: 3')
    expect(source).toContain('CHALLENGE_RULES_SCHEMA_VERSION: Record<GameModeValue, number> = { Ctf: 2')
    expect(source).toContain('CtfInteraction = { FlagSubmission: 0, PatchVerification: 1 }')
    expect(source).toContain('model.runtime.flagSource = FlagSource.Static')
    expect(source).toContain('DEFAULT_CTF_PATCH_UPLOAD_BYTES = 64 * 1024 * 1024')
    expect(runtime).toContain('interactionKind === CtfInteraction.PatchVerification')
    expect(runtime).toContain('runtime.flagSource = patchVerification ? FlagSource.Static : FlagSource.PerTeam')
  })

  test('uses generated endpoints with bounded polling and server-computed availability', async () => {
    const panel = await sourceFile(
      new URL('../app/features/challenges/panels/CtfPanel.vue', import.meta.url),
    ).text()
    const fix = await sourceFile(
      new URL('../app/features/challenges/FixSubmit.vue', import.meta.url),
    ).text()

    expect(panel).toContain("props.challenge.interactionKind === 'PatchVerification'")
    expect(panel).toContain('props.challenge.patchVerificationAvailable === true')
    expect(panel).toContain('getPatchVerificationEndpoint')
    expect(panel).toContain('usePolling(refreshPatchVerification')
    expect(fix).toContain('requestPatchVerificationTargetEndpoint')
    expect(fix).toContain('uploadPatchVerificationEndpoint')
  })

  test('places the platform switch in the existing workspace navigation', async () => {
    const navigation = await sourceFile(
      new URL('../app/features/routes/admin/useAdminPlatformPage.ts', import.meta.url),
    ).text()
    const experiments = await sourceFile(
      new URL('../app/features/routes/admin/platform/AdminPlatformExperimentsPage.vue', import.meta.url),
    ).text()

    expect(navigation).toContain("'/admin/platform/experiments'")
    expect(experiments).toContain('AdminPlatformExperimentsPageView.vue')
  })
})
