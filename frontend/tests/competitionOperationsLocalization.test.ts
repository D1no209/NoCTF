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
    expect(i18n.global.t('admin.qqBot.competition.warnings.global_plugin_disabled')).not.toContain('global_plugin_disabled')
    expect(i18n.global.t('admin.qqBot.competition.warnings.no_online_agent')).not.toContain('no_online_agent')
    expect(i18n.global.t('admin.qqBot.competition.warnings.no_bound_group')).not.toContain('no_bound_group')
  })
})
