import { describe, expect, test } from 'bun:test'

describe('challenge flag match kind editor', () => {
  test('uses the generated match-kind contract in template and competition editors', async () => {
    const templatePage = await Bun.file(
      new URL('../app/pages/admin/challenges/[id].vue', import.meta.url),
    ).text()
    const competitionPage = await Bun.file(
      new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url),
    ).text()

    for (const page of [templatePage, competitionPage]) {
      expect(page).toContain('NoCtfapiEndpointsAdministrationChallengeBankChallengeFlagMatchKindProtocol')
      expect(page).toContain('matchKind: flagForm')
      expect(page).toContain('supportsRegularExpression')
      expect(page).toContain('value="RegularExpression"')
      expect(page).toContain('usesRuntimeFlagInjection')
      expect(page).toContain("$t('环境变量注入')")
      expect(page).toContain('无需维护精确或正则 Flag')
    }
  })
})
