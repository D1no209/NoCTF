import { sourceFile } from './support/feature-source'
import { afterEach, describe, expect, test } from 'bun:test'
import { readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { effect, stop } from 'vue'
import { bloodRankLabel } from "../app/features/leaderboard/types"
import { competitionQuestionRoleLabel } from '../app/lib/competition-question'
import { englishMessages } from '../app/locales/en'
import { CompetitionStatusLabel, enumLabel } from '../app/utils/admin-format'
import { ATTACK_REWARD_MODES, competitionConfigFields } from '../app/utils/game-config'
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
    expect(translate('ui.competitionAdmin')).toBe('竞赛管理')

    setLocale('en')
    expect(translate('ui.competitionAdmin')).toBe('Competition Admin')
  })

  test('keeps user data intact while interpolating localized text', () => {
    setLocale('en')
    expect(translate('ui.teamHasBeenBanned2', { team: 'AAA' }))
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
    const runner = effect(() => rendered.push(translate('ui.competitionAdmin')))

    setLocale('en')

    expect(rendered).toEqual(['竞赛管理', 'Competition Admin'])
    stop(runner)
  })

  test('evaluates reusable labels in the current locale without reloading their modules', () => {
    setLocale('zh-CN')
    const rendered: string[][] = []
    const runner = effect(() => rendered.push([
      enumLabel(CompetitionStatusLabel, 'Draft'),
      bloodRankLabel('First'),
      translate(ATTACK_REWARD_MODES[0].label),
      competitionConfigFields('Ctf')[0]?.label ?? '',
      translate(competitionQuestionRoleLabel.Judge),
    ]))

    setLocale('en')

    expect(rendered).toEqual([
      ['草稿', '一血', '每次攻击固定得分', '默认分值曲线', '裁判'],
      ['Draft', 'First Blood', 'Fixed score for each attack', 'Default score curve', 'Judge'],
    ])
    stop(runner)
  })

  test('provides English resources for every localized Chinese UI key', async () => {
    const files = sourceFiles(fileURLToPath(new URL('../app', import.meta.url)))
    const callPattern = /(?:translate|\$t|\bt)\(\s*(['"])((?:\\.|(?!\1).)*)\1/g
    const missing = new Set<string>()

    for (const file of files) {
      const source = await sourceFile(file).text()
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
  test('renders the language switch in the center capsule', async () => {
    const layout = await sourceFile(
      new URL('../app/layouts/default.vue', import.meta.url),
    ).text()

    const center = layout.slice(layout.indexOf('data-position="center"'), layout.indexOf('data-position="right"'))
    expect(center).toContain('<component :is="LanguageToggle" />')
    expect(center).not.toContain('<component :is="ThemeToggle" />')
  })

  test('switches the selected locale without reloading or remounting the SPA page', async () => {
    const composable = await sourceFile(
      new URL('../app/composables/useLocale.ts', import.meta.url),
    ).text()
    const app = await sourceFile(
      new URL('../app/app.vue', import.meta.url),
    ).text()

    expect(composable).toContain("setLocale(isEnglish.value ? 'zh-CN' : 'en')")
    expect(composable).not.toContain('window.location.reload()')
    expect(app).toContain('<NuxtPage :transition="pageTransition" />')
    expect(app).not.toContain('const { locale } = useLocale()')
    expect(app).not.toMatch(/<NuxtPage\s+[^>]*:key=/)
  })

  test('renders localized dynamic labels and question subjects after switching', async () => {
    const configInput = await sourceFile(
      new URL('../app/features/admin/ConfigFieldInput.vue', import.meta.url),
    ).text()
    const questions = await sourceFile(
      new URL('../app/pages/competitions/[id]/questions.vue', import.meta.url),
    ).text()
    const scoreTrend = await sourceFile(
      new URL('../app/features/leaderboard/ScoreTrendChart.vue', import.meta.url),
    ).text()

    expect(configInput).toContain('{{ $t(option.label) }}')
    expect(questions).toContain("$t('ui.challenge2'")
    expect(questions).toContain("$t('ui.challengeQuestion'")
    expect(questions).not.toContain('`题目 · ${')
    expect(questions).not.toContain('`题目咨询 · ${')
    expect(scoreTrend).toContain('props.title, locale.value')
  })

  test('keeps controller catalogs as keys and remounts cached controls after a locale switch', async () => {
    const logs = await sourceFile(new URL('../app/features/routes/admin/platform/AdminPlatformLogsPage.vue', import.meta.url)).text()
    const select = await Bun.file(new URL('../app/components/ui/select/Select.vue', import.meta.url)).text()
    const toggle = await Bun.file(new URL('../app/components/ui/switch/Switch.vue', import.meta.url)).text()

    expect(logs).toContain('Warning: "ui.warning"')
    expect(logs).toContain('Worker: "ui.worker"')
    expect(logs).not.toContain('Warning: translate(')
    expect(select).toContain(':key="localeKey"')
    expect(toggle).toContain(':key="localeKey"')
  })
})
