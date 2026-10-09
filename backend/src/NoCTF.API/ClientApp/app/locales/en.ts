import CoreMessages from './catalogs/en/core.json'
import CompetitionsMessages from './catalogs/en/competitions.json'
import ChallengesMessages from './catalogs/en/challenges.json'
import LeaderboardMessages from './catalogs/en/leaderboard.json'
import AdministrationMessages from './catalogs/en/administration.json'
import AccountMessages from './catalogs/en/account.json'
import NotificationsMessages from './catalogs/en/notifications.json'
import RuntimeMessages from './catalogs/en/runtime.json'
import WriteupsMessages from './catalogs/en/writeups.json'
import PasskeyMessages from './catalogs/en/passkeys.json'
import MfaMessages from './catalogs/en/mfa.json'
import ApiMessages from './catalogs/en/api.json'
import LiveSoloMessages from './catalogs/en/live-solo.json'

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
  ...ApiMessages,
  ...MfaMessages,
  ...PasskeyMessages,
  ...LiveSoloMessages,
}
export type MessageKey = keyof typeof englishMessages
