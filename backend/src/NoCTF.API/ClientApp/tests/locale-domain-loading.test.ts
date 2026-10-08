import { describe, expect, test } from 'bun:test'
import { localeDomainsForPath } from '../app/locales/route-domains'
import { sourceFile } from './support/feature-source'

describe('locale feature catalogs', () => {
  test('loads only the feature domains required by public routes', () => {
    expect(localeDomainsForPath('/')).toEqual(['account'])
    expect(localeDomainsForPath('/competitions')).toEqual(['account', 'competitions', 'writeups'])
    expect(localeDomainsForPath('/competitions/c1/my/team')).toEqual([
      'account', 'competitions', 'writeups', 'runtime',
    ])
    expect(localeDomainsForPath('/competitions/c1/challenges/c2')).toEqual([
      'account',
      'competitions',
      'writeups',
      'challenges',
      'runtime',
    ])
    expect(localeDomainsForPath('/competitions/c1/leaderboard')).toEqual([
      'account',
      'competitions',
      'writeups',
      'leaderboard',
    ])
    expect(localeDomainsForPath('/notifications')).toEqual(['account', 'notifications'])
    expect(localeDomainsForPath('/users/user-1')).toEqual(['account', 'competitions'])
    expect(localeDomainsForPath('/competitions/c1/challenge-writeups/c2')).toEqual(['account', 'competitions', 'writeups', 'challenges'])
  })

  test('keeps complete catalogs out of the runtime i18n entry', async () => {
    const runtime = await sourceFile(new URL('../app/utils/i18n.ts', import.meta.url)).text()
    expect(runtime).not.toMatch(/^import \{[^}]+\} from '\.\.\/locales\/en'/m)
    expect(runtime).not.toMatch(/^import \{[^}]+\} from '\.\.\/locales\/zh-CN'/m)
    expect(runtime).toContain("import type { MessageKey } from '../locales/en'")
    expect(runtime).not.toContain('englishMessageSources')
  })
})
