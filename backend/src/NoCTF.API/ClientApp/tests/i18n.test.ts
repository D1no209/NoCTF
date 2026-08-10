import { afterEach, describe, expect, test } from 'bun:test'
import { setLocale, translate } from '../app/utils/i18n'

describe('platform locale', () => {
  afterEach(() => setLocale('zh-CN'))

  test('uses source Chinese in the default locale and English resources after switching', () => {
    setLocale('zh-CN')
    expect(translate('竞赛管理')).toBe('竞赛管理')

    setLocale('en')
    expect(translate('竞赛管理')).toBe('Competition Admin')
  })

  test('keeps user data intact while interpolating localized text', () => {
    setLocale('en')
    expect(translate('队伍「{team}」已被封禁', { team: 'AAA' }))
      .toBe('队伍「AAA」已被封禁')
  })
})

describe('locale switch placement', () => {
  test('renders the language switch directly beside the theme switch', async () => {
    const layout = await Bun.file(
      new URL('../app/layouts/default.vue', import.meta.url),
    ).text()

    expect(layout).toContain('<ThemeToggle />\n          <LanguageToggle />')
  })

  test('persists the selected locale before reloading the SPA', async () => {
    const composable = await Bun.file(
      new URL('../app/composables/useLocale.ts', import.meta.url),
    ).text()

    expect(composable).toContain("setLocale(isEnglish.value ? 'zh-CN' : 'en')")
    expect(composable).toContain('window.location.reload()')
  })
})
