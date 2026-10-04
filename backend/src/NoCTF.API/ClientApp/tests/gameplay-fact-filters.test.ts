import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('administrator gameplay fact filters', () => {
  test('uses generated protocol values without type assertions', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/submissions.vue', import.meta.url),
    ).text()

    expect(page).toContain("{ value: 'FlagAttempt', label: 'Flag' }")
    expect(page).toContain("{ value: 'BreakAttempt', label: 'Break' }")
    expect(page).toContain("{ value: 'FixAttempt', label: translate('common.label.patchVerification') }")
    expect(page).toContain('gameplayFactKind: filterKind.value || undefined')
    expect(page).toContain('state: filterState.value || undefined')
    expect(page).toContain('gameplayFactResult: filterResult.value || undefined')
    expect(page).not.toContain('filterKind.value as NoCTFAPIEndpointsGameplayFactsGameplayFactKindProtocol')
    const resultOptions = page.slice(
      page.indexOf('const gameplayFactResultOptions'),
      page.indexOf('] satisfies', page.indexOf('const gameplayFactResultOptions')),
    )
    expect(resultOptions).not.toContain('PlatformFailed')
  })
})
