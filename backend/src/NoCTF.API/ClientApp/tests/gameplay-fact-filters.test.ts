import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('administrator gameplay fact filters', () => {
  test('uses generated protocol values without type assertions', async () => {
    const page = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/submissions.vue', import.meta.url),
    ).text()

    expect(page).toContain("{ value: 'FlagAttempt', label: 'Flag' }")
    expect(page).toContain("{ value: 'BreakAttempt', label: 'Break' }")
    expect(page).toContain("{ value: 'FixAttempt', label: 'Fix' }")
    expect(page).toContain('gameplayFactKind: filterKind.value || null')
    expect(page).toContain('state: filterState.value || null')
    expect(page).toContain('gameplayFactResult: filterResult.value || null')
    expect(page).not.toContain('filterKind.value as NoCtfapiEndpointsGameplayFactsGameplayFactKindProtocol')
    const resultOptions = page.slice(
      page.indexOf('const gameplayFactResultOptions'),
      page.indexOf('] satisfies', page.indexOf('const gameplayFactResultOptions')),
    )
    expect(resultOptions).not.toContain('PlatformFailed')
  })
})
