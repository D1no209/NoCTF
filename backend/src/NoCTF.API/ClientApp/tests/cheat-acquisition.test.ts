import { afterEach, describe, expect, test } from 'bun:test'
import { cheatEvidenceSourceLabel, cheatOwnerTeamLabel } from '../app/lib/cheat-acquisition'
import { gameplayFactFailureCodeLabel } from '../app/utils/labels'
import { ensureLocaleDomains, prepareLocale, setLocale } from '../app/utils/i18n'

afterEach(() => setLocale('zh-CN'))

describe('static Flag acquisition evidence', () => {
  test('keeps ownership ambiguity distinct from missing acquisition evidence', async () => {
    await ensureLocaleDomains(['administration'])
    await prepareLocale('en')
    setLocale('en')
    expect(cheatOwnerTeamLabel({ failureCode: 'StaticFlagWithoutAttachment', ownerTeamName: null })).toBe('Not applicable')
    expect(cheatOwnerTeamLabel({ failureCode: 'ForeignTeamFlagDetected', ownerTeamName: 'Owner' })).toBe('Owner')
    expect(cheatOwnerTeamLabel({ failureCode: 'ForeignTeamFlagDetected', ownerTeamName: null })).not.toBe('Not applicable')
    expect(cheatEvidenceSourceLabel('LegacySubmission')).toBe('Earlier submission, treated as acquired')
    expect(cheatEvidenceSourceLabel('Recorded')).toBe('Recorded at submission')
  })

  test('shows separate and combined reasons in both languages', async () => {
    await ensureLocaleDomains(['administration'])
    for (const locale of ['en', 'zh-CN'] as const) {
      await prepareLocale(locale)
      setLocale(locale)
      const reasons = ['StaticFlagWithoutContainer', 'StaticFlagWithoutAttachment', 'StaticFlagWithoutContainerAndAttachment'] as const
      const labels = reasons.map(gameplayFactFailureCodeLabel)
      expect(new Set(labels).size).toBe(3)
      for (const label of labels) { expect(label).not.toContain('cheats.reason.'); expect(label.length).toBeGreaterThan(5) }
    }
  })
})
