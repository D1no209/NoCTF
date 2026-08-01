import { describe, expect, test } from 'bun:test'
import { createI18n } from 'vue-i18n'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

describe('login localization', () => {
  test.each([
    ['en', en, 'name@example.com or johndoe'],
    ['zh-CN', zhCN, 'name@example.com 或 johndoe'],
  ])('renders the identifier placeholder for %s without a message compilation error', (locale, messages, expected) => {
    const i18n = createI18n({
      legacy: false,
      locale,
      messages: { [locale]: messages },
    })

    expect(i18n.global.t('auth.loginIdentifierPlaceholder')).toBe(expected)
  })

  test.each([
    ['en', en, 'Show password', 'Hide password'],
    ['zh-CN', zhCN, '显示密码', '隐藏密码'],
  ])('provides password visibility labels for %s', (locale, messages, showLabel, hideLabel) => {
    const i18n = createI18n({
      legacy: false,
      locale,
      messages: { [locale]: messages },
    })

    expect(i18n.global.t('auth.showPassword')).toBe(showLabel)
    expect(i18n.global.t('auth.hidePassword')).toBe(hideLabel)
  })
})
