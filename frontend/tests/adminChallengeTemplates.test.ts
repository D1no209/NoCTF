import type {
  NoCtfDomainChallengesChallengeVisibility,
  NoCtfDomainCompetitionsGameMode,
} from '../src/api/generated/types.gen'
import { describe, expect, test } from 'bun:test'
import { createI18n } from 'vue-i18n'
import {
  canDeleteTemplate,
  canRestoreTemplate,
  CHALLENGE_MODE,
  CHALLENGE_VISIBILITY,
  challengeModeLabelKey,
  challengeVisibilityLabelKey,
  isDeletedTemplate,
} from '../src/components/admin/challenges/challengeTemplatePresentation'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

describe('admin challenge template presentation', () => {
  test('maps response modes and visibility values to localized labels', () => {
    expect(challengeModeLabelKey(CHALLENGE_MODE.ctf)).toBe('admin.challenges.modeCtf')
    expect(challengeModeLabelKey(CHALLENGE_MODE.awd)).toBe('admin.challenges.modeAwd')
    expect(challengeModeLabelKey(CHALLENGE_MODE.awdp)).toBe('admin.challenges.modeAwdp')
    expect(challengeModeLabelKey(CHALLENGE_MODE.koh)).toBe('admin.challenges.modeKoh')
    expect(challengeModeLabelKey(99 as NoCtfDomainCompetitionsGameMode)).toBe(
      'admin.challenges.modeUnknown',
    )

    expect(challengeVisibilityLabelKey(CHALLENGE_VISIBILITY.private)).toBe(
      'admin.challenges.visibilityPrivate',
    )
    expect(challengeVisibilityLabelKey(CHALLENGE_VISIBILITY.shared)).toBe(
      'admin.challenges.visibilityShared',
    )
    expect(challengeVisibilityLabelKey(99 as NoCtfDomainChallengesChallengeVisibility)).toBe(
      'admin.challenges.visibilityUnknown',
    )
  })

  test('derives delete and restore actions from lifecycle state', () => {
    const activeTemplate = {
      deletedAt: null,
      activeCompetitionReferenceCount: 0,
    }
    expect(isDeletedTemplate(activeTemplate)).toBe(false)
    expect(canDeleteTemplate(activeTemplate)).toBe(true)
    expect(canRestoreTemplate(activeTemplate)).toBe(false)

    const referencedTemplate = {
      deletedAt: null,
      activeCompetitionReferenceCount: 1,
    }
    expect(canDeleteTemplate(referencedTemplate)).toBe(false)

    const deletedTemplate = {
      deletedAt: '2026-07-31T00:00:00Z',
      activeCompetitionReferenceCount: 0,
    }
    expect(isDeletedTemplate(deletedTemplate)).toBe(true)
    expect(canDeleteTemplate(deletedTemplate)).toBe(false)
    expect(canRestoreTemplate(deletedTemplate)).toBe(true)

    expect(canDeleteTemplate({ deletedAt: null })).toBe(false)
  })

  test.each([
    ['en', en],
    ['zh-CN', zhCN],
  ])('provides complete lifecycle inventory copy for %s', (locale, messages) => {
    const i18n = createI18n({
      legacy: false,
      locale,
      messages: { [locale]: messages },
    })

    for (const key of [
      ...Object.values(CHALLENGE_MODE).map(challengeModeLabelKey),
      ...Object.values(CHALLENGE_VISIBILITY).map(challengeVisibilityLabelKey),
      'admin.challenges.gitOpsInventory',
      'admin.challenges.lifecycleSubtitle',
      'admin.challenges.includeDeleted',
      'admin.challenges.stableId',
      'admin.challenges.revision',
      'admin.challenges.activeReferences',
      'admin.challenges.statusActive',
      'admin.challenges.statusDeleted',
      'admin.challenges.restore',
      'admin.challenges.restoreKeepsId',
      'admin.challenges.softDeleteNote',
    ]) {
      expect(i18n.global.t(key)).not.toBe(key)
    }

    if (locale === 'en') {
      expect(i18n.global.t('admin.challenges.referenceCount', { count: 1 })).toBe(
        '1 competition',
      )
      expect(i18n.global.t('admin.challenges.referenceCount', { count: 2 })).toBe(
        '2 competitions',
      )
    }
  })
})
