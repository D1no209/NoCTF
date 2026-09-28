import type { MessageKey } from './zh-CN'
import { messages as CoreMessages } from './catalogs/en/core'
import { messages as CompetitionsMessages } from './catalogs/en/competitions'
import { messages as ChallengesMessages } from './catalogs/en/challenges'
import { messages as LeaderboardMessages } from './catalogs/en/leaderboard'
import { messages as AdministrationMessages } from './catalogs/en/administration'
import { messages as AccountMessages } from './catalogs/en/account'
import { messages as NotificationsMessages } from './catalogs/en/notifications'
import { messages as RuntimeMessages } from './catalogs/en/runtime'
import { messages as WriteupsMessages } from './catalogs/en/writeups'

/** Canonical build-time catalog used by architecture and parity checks. */
export const englishMessages = {
  ...CoreMessages,
  ...CompetitionsMessages,
  ...ChallengesMessages,
  ...LeaderboardMessages,
  ...AdministrationMessages,
  ...AccountMessages,
  ...NotificationsMessages,
  ...RuntimeMessages,
  ...WriteupsMessages,
} satisfies Record<MessageKey, string>
