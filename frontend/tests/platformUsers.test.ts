import { describe, expect, test } from 'bun:test'
import { createI18n } from 'vue-i18n'
import {
  BOT_TOKEN_LIFETIMES,
  isBot,
  isHuman,
  isValidBotUserName,
  PLATFORM_USER_KIND,
  PLATFORM_USER_ROLE,
  userKindLabelKey,
  userRoleLabelKey,
} from '../src/components/admin/users/platformUserPresentation'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

describe('platform user presentation', () => {
  test('maps bounded user kinds and roles to localized labels', () => {
    expect(userKindLabelKey(PLATFORM_USER_KIND.human)).toBe('admin.users.kindHuman')
    expect(userKindLabelKey(PLATFORM_USER_KIND.bot)).toBe('admin.users.kindBot')
    expect(userKindLabelKey(undefined)).toBe('admin.users.kindUnknown')
    expect(userRoleLabelKey(PLATFORM_USER_ROLE.user)).toBe('admin.users.roleUser')
    expect(userRoleLabelKey(PLATFORM_USER_ROLE.organizer)).toBe('admin.users.roleOrganizer')
    expect(userRoleLabelKey(PLATFORM_USER_ROLE.administrator)).toBe('admin.users.roleAdmin')
  })

  test('keeps Bot and Human actions mutually exclusive', () => {
    expect(isBot({ kind: PLATFORM_USER_KIND.bot })).toBe(true)
    expect(isHuman({ kind: PLATFORM_USER_KIND.bot })).toBe(false)
    expect(isBot({ kind: PLATFORM_USER_KIND.human })).toBe(false)
    expect(isHuman({ kind: PLATFORM_USER_KIND.human })).toBe(true)
    expect(isBot({})).toBe(false)
    expect(isHuman({})).toBe(false)
  })

  test('accepts only server-compatible Bot user names', () => {
    expect(isValidBotUserName('gitops_bot-01')).toBe(true)
    expect(isValidBotUserName(' ab1 ')).toBe(true)
    expect(isValidBotUserName('ab')).toBe(false)
    expect(isValidBotUserName('bot name')).toBe(false)
    expect(isValidBotUserName('机器人')).toBe(false)
  })

  test('offers only server-supported bounded token lifetimes', () => {
    expect(BOT_TOKEN_LIFETIMES.map(option => option.seconds)).toEqual([
      86_400,
      2_592_000,
      7_776_000,
      31_536_000,
    ])
    expect(BOT_TOKEN_LIFETIMES.every(option => option.seconds >= 60)).toBe(true)
    expect(BOT_TOKEN_LIFETIMES.every(option => option.seconds <= 31_536_000)).toBe(true)
  })

  test.each([
    ['en', en],
    ['zh-CN', zhCN],
  ])('provides complete platform user copy for %s', (locale, messages) => {
    const i18n = createI18n({
      legacy: false,
      locale,
      messages: { [locale]: messages },
    })

    for (const key of [
      userKindLabelKey(PLATFORM_USER_KIND.human),
      userKindLabelKey(PLATFORM_USER_KIND.bot),
      userRoleLabelKey(PLATFORM_USER_ROLE.user),
      userRoleLabelKey(PLATFORM_USER_ROLE.organizer),
      userRoleLabelKey(PLATFORM_USER_ROLE.administrator),
      ...BOT_TOKEN_LIFETIMES.map(option => option.labelKey),
      'admin.users.createBot',
      'admin.users.issueToken',
      'admin.users.invalidateTokens',
      'admin.users.tokenOneTimeWarning',
    ]) {
      expect(i18n.global.t(key)).not.toBe(key)
    }
  })
})
