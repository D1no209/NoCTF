import { describe, expect, test } from 'bun:test'
import { formatDocumentTitle, routeTitleKey } from '../app/features/shell/page-title'

describe('browser page titles', () => {
  test('uses the platform name on the home page and a localized page label elsewhere', () => {
    expect(routeTitleKey('/')).toBeNull()
    expect(formatDocumentTitle('NoCTF · MOCK', null)).toBe('NoCTF · MOCK')
    expect(formatDocumentTitle('NoCTF · MOCK', '题目')).toBe('题目 · NoCTF · MOCK')
  })

  test('maps public and administration routes to their navigation catalog labels', () => {
    expect(routeTitleKey('/competitions')).toBe('common.label.competitions')
    expect(routeTitleKey('/competitions/competition-1/challenges')).toBe('common.label.challenge.pageTitle')
    expect(routeTitleKey('/competitions/competition-1/my/team')).toBe('competitions.label.myTeam')
    expect(routeTitleKey('/admin/challenges/template-1')).toBe('common.label.challengeLibrary')
    expect(routeTitleKey('/admin/competitions/competition-1/permissions')).toBe('administration.label.permissions')
    expect(routeTitleKey('/admin/platform/runtimes')).toBe('common.label.runtimeContainers')
    expect(routeTitleKey('/auth/login')).toBe('auth.login.action')
  })
})
