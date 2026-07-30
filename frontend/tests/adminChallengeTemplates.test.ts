import type {
  NoCtfDomainChallengesChallengeVisibility,
  NoCtfDomainCompetitionsGameMode,
} from '../src/api/generated/types.gen'
import { describe, expect, test } from 'bun:test'
import { createI18n } from 'vue-i18n'
import {
  buildChallengeGitOpsAccessUpdate,
  existingChallengeManagerIds,
  gitOpsBotCandidates,
} from '../src/components/admin/challenges/challengeGitOpsAccess'
import {
  canDeleteTemplate,
  canRestoreTemplate,
  CHALLENGE_MODE,
  CHALLENGE_VISIBILITY,
  challengeModeLabelKey,
  challengeVisibilityLabelKey,
  isDeletedTemplate,
} from '../src/components/admin/challenges/challengeTemplatePresentation'
import {
  PLATFORM_USER_KIND,
  PLATFORM_USER_ROLE,
} from '../src/components/admin/users/platformUserPresentation'
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

  test('adds only an unassigned Organizer Bot while preserving existing Managers', () => {
    const template = {
      id: 'challenge-01',
      ownerId: 'owner-01',
      managerIds: ['manager-02', 'owner-01', 'manager-01', 'manager-01'],
      revision: 7,
    }
    const users = [
      {
        id: 'owner-01',
        userName: 'owner-bot',
        kind: PLATFORM_USER_KIND.bot,
        role: PLATFORM_USER_ROLE.organizer,
      },
      {
        id: 'manager-01',
        userName: 'assigned-bot',
        kind: PLATFORM_USER_KIND.bot,
        role: PLATFORM_USER_ROLE.organizer,
      },
      {
        id: 'bot-02',
        userName: 'zeta-bot',
        kind: PLATFORM_USER_KIND.bot,
        role: PLATFORM_USER_ROLE.organizer,
      },
      {
        id: 'bot-01',
        userName: 'alpha-bot',
        kind: PLATFORM_USER_KIND.bot,
        role: PLATFORM_USER_ROLE.organizer,
      },
      {
        id: 'human-organizer',
        userName: 'human',
        kind: PLATFORM_USER_KIND.human,
        role: PLATFORM_USER_ROLE.organizer,
      },
      {
        id: 'ordinary-bot',
        userName: 'ordinary-bot',
        kind: PLATFORM_USER_KIND.bot,
        role: PLATFORM_USER_ROLE.user,
      },
    ]

    expect(existingChallengeManagerIds(template)).toEqual(['manager-01', 'manager-02'])
    expect(gitOpsBotCandidates(users, template).map(user => user.id)).toEqual([
      'bot-01',
      'bot-02',
    ])
    expect(buildChallengeGitOpsAccessUpdate(template, 'bot-01')).toEqual({
      challengeId: 'challenge-01',
      managerIds: ['bot-01', 'manager-01', 'manager-02'],
      expectedRevision: 7,
    })
    expect(buildChallengeGitOpsAccessUpdate(template, 'owner-01')).toBeNull()
    expect(
      buildChallengeGitOpsAccessUpdate({ ...template, revision: undefined }, 'bot-01'),
    ).toBeNull()
    expect(
      buildChallengeGitOpsAccessUpdate({ ...template, managerIds: undefined }, 'bot-01'),
    ).toBeNull()
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
      'admin.challenges.gitOpsAccess',
      'admin.challenges.gitOpsAccessTitle',
      'admin.challenges.gitOpsAccessDescription',
      'admin.challenges.existingManagersPreserved',
      'admin.challenges.organizerBot',
      'admin.challenges.grantGitOpsAccess',
      'admin.challenges.gitOpsAccessRevisionConflict',
      'admin.challenges.gitOpsAccessRoleNotEligible',
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
