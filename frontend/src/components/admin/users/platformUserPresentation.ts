import type {
  NoCtfDomainIdentityUserKind,
  NoCtfDomainIdentityUserRole,
} from '@/api/generated/types.gen'
import type { PlatformUser } from '@/api/noctf'
import { PLATFORM_USER_ROLE } from '@/api/userRole'

export { PLATFORM_USER_ROLE } from '@/api/userRole'

const BOT_USER_NAME_PATTERN = /^[\w-]{3,64}$/

export const PLATFORM_USER_KIND = {
  human: 0,
  bot: 1,
} as const satisfies Record<string, NoCtfDomainIdentityUserKind>

export const BOT_TOKEN_LIFETIMES = [
  { seconds: 86_400, labelKey: 'admin.users.tokenLifetimeDay' },
  { seconds: 2_592_000, labelKey: 'admin.users.tokenLifetimeMonth' },
  { seconds: 7_776_000, labelKey: 'admin.users.tokenLifetimeQuarter' },
  { seconds: 31_536_000, labelKey: 'admin.users.tokenLifetimeYear' },
] as const

export function userKindLabelKey(kind?: NoCtfDomainIdentityUserKind) {
  switch (kind) {
    case PLATFORM_USER_KIND.human:
      return 'admin.users.kindHuman'
    case PLATFORM_USER_KIND.bot:
      return 'admin.users.kindBot'
    default:
      return 'admin.users.kindUnknown'
  }
}

export function userRoleLabelKey(role?: NoCtfDomainIdentityUserRole) {
  switch (role) {
    case PLATFORM_USER_ROLE.organizer:
      return 'admin.users.roleOrganizer'
    case PLATFORM_USER_ROLE.administrator:
      return 'admin.users.roleAdmin'
    default:
      return 'admin.users.roleUser'
  }
}

export function isBot(user: PlatformUser) {
  return user.kind === PLATFORM_USER_KIND.bot
}

export function isHuman(user: PlatformUser) {
  return user.kind === PLATFORM_USER_KIND.human
}

export function isValidBotUserName(value: string) {
  return BOT_USER_NAME_PATTERN.test(value.trim())
}
