import { englishMessages } from './en'
import CoreMessages from './catalogs/zh-CN/core.json'
import CompetitionsMessages from './catalogs/zh-CN/competitions.json'
import ChallengesMessages from './catalogs/zh-CN/challenges.json'
import LeaderboardMessages from './catalogs/zh-CN/leaderboard.json'
import AdministrationMessages from './catalogs/zh-CN/administration.json'
import AccountMessages from './catalogs/zh-CN/account.json'
import NotificationsMessages from './catalogs/zh-CN/notifications.json'
import RuntimeMessages from './catalogs/zh-CN/runtime.json'
import WriteupsMessages from './catalogs/zh-CN/writeups.json'
import ApiMessages from './catalogs/zh-CN/api.json'

const translatedMessages = {
  ...CoreMessages,
  ...CompetitionsMessages,
  ...ChallengesMessages,
  ...LeaderboardMessages,
  ...AdministrationMessages,
  ...AccountMessages,
  ...NotificationsMessages,
  ...RuntimeMessages,
  ...WriteupsMessages,
  ...ApiMessages,
}
export const chineseMessages = {
  ...englishMessages,
  ...Object.fromEntries(Object.entries(translatedMessages).filter(([, value]) => value.trim())),
}
export type { MessageKey } from './en'
