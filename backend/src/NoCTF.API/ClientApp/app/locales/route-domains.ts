import type { LocaleDomain } from '~/utils/i18n'

const administrationDomains: readonly LocaleDomain[] = [
  'administration',
  'competitions',
  'challenges',
  'leaderboard',
  'account',
  'notifications',
  'runtime',
  'writeups',
  'live-solo',
]

/** Maps routes to feature catalogs without coupling individual views to loading. */
export function localeDomainsForPath(path: string): readonly LocaleDomain[] {
  if (path.startsWith('/admin') || path.includes('/staff'))
    return administrationDomains

  // The account panel belongs to the global application shell and can open on
  // every route, so its catalog must be ready before the shell renders.
  const domains = new Set<LocaleDomain>(['account'])
  if (path.startsWith('/competitions')) {
    domains.add('competitions')
    domains.add('writeups')
  }
  if (path.includes('/live-solo')) domains.add('live-solo')
  if (path.includes('/challenges')) {
    domains.add('challenges')
    domains.add('runtime')
    domains.add('writeups')
  }
  if (path.endsWith('/my/team'))
    domains.add('runtime')
  if (path.includes('/leaderboard')) {
    domains.add('leaderboard')
    domains.add('writeups')
  }
  if (path.includes('/notifications') || path.includes('/questions') || path === '/notifications')
    domains.add('notifications')
  if (path.includes('/writeup') || path.includes('/challenge-writeups')) {
    domains.add('writeups')
    domains.add('challenges')
  }
  if (path.startsWith('/users/')) {
    domains.add('account')
    domains.add('competitions')
  }
  return [...domains]
}
