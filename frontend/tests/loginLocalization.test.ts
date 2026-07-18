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
})
