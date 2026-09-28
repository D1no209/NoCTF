import type { MessageKey } from '../../locales/zh-CN'

const authenticationTitles = {
  login: 'ui.signIn',
  register: 'ui.createAccount',
  'password-reset': 'ui.resetPassword',
  'verify-email': 'ui.emailVerification',
} satisfies Record<string, MessageKey>

const competitionTitles = {
  challenges: 'ui.challenge',
  leaderboard: 'ui.leaderboard',
  questions: 'ui.questions',
  events: 'ui.activity',
  teams: 'ui.teams',
  live: 'ui.3dLiveScreen',
  'awdp-live': 'ui.controlScreen',
} satisfies Record<string, MessageKey>

const competitionAdministrationTitles = {
  configuration: 'ui.configuration',
  tracks: 'ui.tracks',
  challenges: 'ui.challenge',
  teams: 'ui.teamManagement',
  submissions: 'ui.submissions',
  writeup: 'writeUp.myWriteUp',
  writeups: 'writeUp.review',
  runtimes: 'ui.runtime',
  'traffic-captures': 'runtime.trafficCaptures',
  cheats: 'ui.cheating',
  leaderboard: 'ui.leaderboard',
  exports: 'ui.export',
  permissions: 'ui.permissions',
} satisfies Record<string, MessageKey>

const platformAdministrationTitles = {
  users: 'ui.user',
  bots: 'ui.bot',
  email: 'ui.emailAndHumanVerification',
  runtimes: 'ui.runtimeContainers',
  logs: 'ui.log',
  audit: 'ui.audit',
} satisfies Record<string, MessageKey>

/** Maps stable route segments to the same catalog labels used by in-page navigation. */
export function routeTitleKey(pathname: string): MessageKey | null {
  const segments = pathname.split('/').filter(Boolean)
  if (!segments.length) return null

  if (segments[0] === 'auth')
    return authenticationTitles[segments[1] as keyof typeof authenticationTitles] ?? null

  if (segments[0] === 'notifications') return 'ui.notifications'
  if (segments[0] === 'users') return 'ui.user'

  if (segments[0] === 'competitions') {
    if (segments.length < 3) return 'ui.competitions'
    if (segments[2] === 'my')
      return segments[3] === 'submissions' ? 'ui.mySubmissions' : 'ui.myTeam'
    return competitionTitles[segments[2] as keyof typeof competitionTitles] ?? 'ui.competitions'
  }

  if (segments[0] !== 'admin') return null
  if (segments[1] === 'challenges') return 'ui.challengeLibrary'
  if (segments[1] === 'competitions') {
    if (segments.length < 4) return 'ui.competitionAdmin'
    return competitionAdministrationTitles[segments[3] as keyof typeof competitionAdministrationTitles]
      ?? 'ui.competitionAdmin'
  }
  if (segments[1] === 'platform') {
    if (segments.length < 3) return 'ui.platformAdmin'
    return platformAdministrationTitles[segments[2] as keyof typeof platformAdministrationTitles]
      ?? 'ui.platformAdmin'
  }
  return null
}

export function formatDocumentTitle(platformName: string | null | undefined, pageLabel: string | null): string {
  const brand = platformName?.trim() || 'NoCTF'
  return pageLabel?.trim() ? `${pageLabel.trim()} · ${brand}` : brand
}
