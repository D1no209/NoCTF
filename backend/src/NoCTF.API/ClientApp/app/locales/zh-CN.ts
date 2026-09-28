import { messages as CoreMessages } from './catalogs/zh-CN/core'
import { messages as CompetitionsMessages } from './catalogs/zh-CN/competitions'
import { messages as ChallengesMessages } from './catalogs/zh-CN/challenges'
import { messages as LeaderboardMessages } from './catalogs/zh-CN/leaderboard'
import { messages as AdministrationMessages } from './catalogs/zh-CN/administration'
import { messages as AccountMessages } from './catalogs/zh-CN/account'
import { messages as NotificationsMessages } from './catalogs/zh-CN/notifications'
import { messages as RuntimeMessages } from './catalogs/zh-CN/runtime'
import { messages as WriteupsMessages } from './catalogs/zh-CN/writeups'

/** Canonical build-time catalog. Runtime code loads feature chunks from locales/catalogs. */
export const chineseMessages = {
  ...CoreMessages,
  ...CompetitionsMessages,
  ...ChallengesMessages,
  ...LeaderboardMessages,
  ...AdministrationMessages,
  ...AccountMessages,
  ...NotificationsMessages,
  ...RuntimeMessages,
  ...WriteupsMessages,
} as const
export type MessageKey = keyof typeof chineseMessages
