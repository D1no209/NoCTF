import { afterEach, describe, expect, test } from 'bun:test'
import { readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { effect, stop } from 'vue'
import { englishMessages } from '../app/locales/en'
import { localeTag, setLocale, translate } from '../app/utils/i18n'

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((name) => {
    const path = join(directory, name)
    if (path.includes(`${join('app', 'api')}`) || path.includes(`${join('app', 'locales')}`))
      return []
    return statSync(path).isDirectory()
      ? sourceFiles(path)
      : /\.(ts|vue)$/.test(path) ? [path] : []
  })
}

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
      .toBe('Team “AAA” has been banned')
  })

  test('uses the selected locale for dates and numbers', () => {
    setLocale('zh-CN')
    expect(localeTag()).toBe('zh-CN')
    setLocale('en')
    expect(localeTag()).toBe('en-US')
  })

  test('reactively updates translated consumers in place', () => {
    setLocale('zh-CN')
    const rendered: string[] = []
    const runner = effect(() => rendered.push(translate('竞赛管理')))

    setLocale('en')

    expect(rendered).toEqual(['竞赛管理', 'Competition Admin'])
    stop(runner)
  })

  test('provides English resources for every localized Chinese UI key', async () => {
    const files = sourceFiles(fileURLToPath(new URL('../app', import.meta.url)))
    const callPattern = /(?:translate|\$t|\bt)\(\s*(['"])((?:\\.|(?!\1).)*)\1/g
    const missing = new Set<string>()

    for (const file of files) {
      const source = await Bun.file(file).text()
      for (const match of source.matchAll(callPattern)) {
        const key = match[2]!.replace(/\\'/g, "'").replace(/\\"/g, '"')
        if (/\p{Script=Han}/u.test(key) && !(key in englishMessages))
          missing.add(key)
      }
    }

    expect([...missing]).toEqual([])
    expect(Object.values(englishMessages).some(value => /\p{Script=Han}/u.test(value))).toBe(false)
  })
})

describe('locale switch placement', () => {
  test('renders the language switch directly beside the theme switch', async () => {
    const layout = await Bun.file(
      new URL('../app/layouts/default.vue', import.meta.url),
    ).text()

    expect(layout).toContain('<ThemeToggle />\n          <LanguageToggle />')
  })

  test('switches the selected locale without reloading the SPA', async () => {
    const composable = await Bun.file(
      new URL('../app/composables/useLocale.ts', import.meta.url),
    ).text()
    const app = await Bun.file(
      new URL('../app/app.vue', import.meta.url),
    ).text()

    expect(composable).toContain("setLocale(isEnglish.value ? 'zh-CN' : 'en')")
    expect(composable).not.toContain('window.location.reload()')
    expect(app).toContain('const { locale } = useLocale()')
    expect(app).toContain('<NuxtPage :key="locale" />')
  })
})
