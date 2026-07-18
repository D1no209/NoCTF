import { describe, expect, test } from 'bun:test'
import { createI18n } from 'vue-i18n'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

describe('competition operations localization', () => {
  test.each([
    ['en', en, 'Operations', 'Competition operations'],
    ['zh-CN', zhCN, '赛事运维', '赛事运维'],
  ])('provides navigation and workspace copy for %s', (locale, messages, navigation, title) => {
    const i18n = createI18n({
      legacy: false,
      locale,
      messages: { [locale]: messages },
    })

    expect(i18n.global.t('admin.competitionDetail.navOperations')).toBe(navigation)
    expect(i18n.global.t('admin.competitionOperations.title')).toBe(title)
    expect(i18n.global.t('admin.qqBot.competition.deliveryControls')).not.toContain('admin.qqBot')
  })
})
