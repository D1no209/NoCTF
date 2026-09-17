import { describe, expect, test } from 'bun:test'
import { localeDomainsForPath } from '../app/locales/route-domains'
import { sourceFile } from './support/feature-source'

describe('locale feature catalogs', () => {
  test('loads only the feature domains required by public routes', () => {
    expect(localeDomainsForPath('/')).toEqual(['account'])
    expect(localeDomainsForPath('/competitions')).toEqual(['account', 'competitions'])
    expect(localeDomainsForPath('/competitions/c1/challenges/c2')).toEqual([
      'account',
      'competitions',
      'challenges',
      'runtime',
    ])
    expect(localeDomainsForPath('/competitions/c1/leaderboard')).toEqual([
      'account',
      'competitions',
      'leaderboard',
    ])
    expect(localeDomainsForPath('/notifications')).toEqual(['account', 'notifications'])
  })

  test('keeps complete catalogs out of the runtime i18n entry', async () => {
    const runtime = await sourceFile(new URL('../app/utils/i18n.ts', import.meta.url)).text()
    expect(runtime).not.toContain("from '../locales/en'")
    expect(runtime).not.toMatch(/^import \{[^}]+\} from '\.\.\/locales\/zh-CN'/m)
    expect(runtime).toContain("import type { MessageKey } from '../locales/zh-CN'")
    expect(runtime).not.toContain('englishMessageSources')
  })
})
