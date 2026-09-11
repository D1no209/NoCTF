import { describe, expect, test } from 'bun:test'
import { formatDocumentTitle, routeTitleKey } from '../app/features/shell/page-title'

describe('browser page titles', () => {
  test('uses the platform name on the home page and a localized page label elsewhere', () => {
    expect(routeTitleKey('/')).toBeNull()
    expect(formatDocumentTitle('NoCTF · MOCK', null)).toBe('NoCTF · MOCK')
    expect(formatDocumentTitle('NoCTF · MOCK', '题目')).toBe('题目 · NoCTF · MOCK')
  })

  test('maps public and administration routes to their navigation catalog labels', () => {
    expect(routeTitleKey('/competitions')).toBe('ui.competitions')
    expect(routeTitleKey('/competitions/competition-1/challenges')).toBe('ui.challenge')
    expect(routeTitleKey('/competitions/competition-1/my/team')).toBe('ui.myTeam')
    expect(routeTitleKey('/admin/challenges/template-1')).toBe('ui.challengeLibrary')
    expect(routeTitleKey('/admin/competitions/competition-1/permissions')).toBe('ui.permissions')
    expect(routeTitleKey('/admin/platform/monitoring')).toBe('ui.monitoring')
    expect(routeTitleKey('/auth/login')).toBe('ui.signIn')
  })
})
