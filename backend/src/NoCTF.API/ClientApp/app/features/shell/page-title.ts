import type { MessageKey } from '../../locales/zh-CN'

const authenticationTitles = {
  login: 'auth.login.action',
  register: 'common.label.createAccount',
  'password-reset': 'common.label.resetPassword',
  'verify-email': 'common.label.emailVerification',
} satisfies Record<string, MessageKey>

const competitionTitles = {
  challenges: 'common.label.challenge.pageTitle',
  leaderboard: 'common.label.leaderboard',
  questions: 'common.label.questions',
  events: 'administration.label.activity',
  teams: 'common.label.teams',
  live: 'leaderboard.ctf.liveTitle',
  'awdp-live': 'administration.label.controlScreen',
} satisfies Record<string, MessageKey>

const competitionAdministrationTitles = {
  configuration: 'administration.label.configuration',
  tracks: 'common.label.tracks',
  challenges: 'common.label.challenge.pageTitle',
  teams: 'common.label.teamManagement',
  submissions: 'common.label.submissions',
  writeup: 'writeUp.myWriteUp',
  writeups: 'writeUp.review',
  runtimes: 'administration.label.runtime',
  'traffic-captures': 'runtime.trafficCaptures',
  cheats: 'administration.label.cheating',
  leaderboard: 'common.label.leaderboard',
  exports: 'administration.label.export',
  permissions: 'administration.label.permissions',
} satisfies Record<string, MessageKey>

const platformAdministrationTitles = {
  users: 'administration.label.user',
  bots: 'administration.label.bot',
  email: 'administration.label.emailHumanVerification',
  runtimes: 'common.label.runtimeContainers',
  logs: 'administration.label.log',
  audit: 'administration.label.audit',
} satisfies Record<string, MessageKey>

/** Maps stable route segments to the same catalog labels used by in-page navigation. */
export function routeTitleKey(pathname: string): MessageKey | null {
  const segments = pathname.split('/').filter(Boolean)
  if (!segments.length) return null

  if (segments[0] === 'auth')
    return authenticationTitles[segments[1] as keyof typeof authenticationTitles] ?? null

  if (segments[0] === 'notifications') return 'common.label.notifications'
  if (segments[0] === 'users') return 'administration.label.user'

  if (segments[0] === 'competitions') {
    if (segments.length < 3) return 'common.label.competitions'
    if (segments[2] === 'my')
      return segments[3] === 'submissions' ? 'competitions.label.mySubmissions' : 'competitions.label.myTeam'
    return competitionTitles[segments[2] as keyof typeof competitionTitles] ?? 'common.label.competitions'
  }

  if (segments[0] !== 'admin') return null
  if (segments[1] === 'challenges') return 'common.label.challengeLibrary'
  if (segments[1] === 'competitions') {
    if (segments.length < 4) return 'navigation.competitionAdmin'
    return competitionAdministrationTitles[segments[3] as keyof typeof competitionAdministrationTitles]
      ?? 'navigation.competitionAdmin'
  }
  if (segments[1] === 'platform') {
    if (segments.length < 3) return 'common.label.platformAdmin'
    return platformAdministrationTitles[segments[2] as keyof typeof platformAdministrationTitles]
      ?? 'common.label.platformAdmin'
  }
  return null
}

export function formatDocumentTitle(platformName: string | null | undefined, pageLabel: string | null): string {
  const brand = platformName?.trim() || 'NoCTF'
  return pageLabel?.trim() ? `${pageLabel.trim()} · ${brand}` : brand
}
