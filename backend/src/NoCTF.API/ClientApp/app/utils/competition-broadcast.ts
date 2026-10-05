import type {
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
} from '../api'
import { translate } from './i18n'
import { competitionChallengePath, competitionTeamsPath } from './app-routes'

export const competitionBroadcastKinds = [
  'FirstBloodAwarded',
  'SecondBloodAwarded',
  'ThirdBloodAwarded',
  'TeamBanned',
  'TeamBanCorrectionPublished',
  'HintPublished',
  'ChallengeDescriptionUpdated',
  'ChallengePublished',
  'AwdpBreakResolved',
  'AwdpFixResolved',
  'AnnouncementPublished',
] satisfies NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol[]

const competitionBroadcastLookbackMs = 30 * 24 * 60 * 60 * 1000
const competitionBroadcastClockSkewMs = 5 * 60 * 1000

export interface CompetitionBroadcastQueryWindow {
  from: string
  to: string
}

/** Keep realtime invalidations inside the REST window even when browser and server clocks differ. */
export function competitionBroadcastQueryWindow(
  startAt: number,
  clientNow: number,
  latestNotifiedAt = 0,
): CompetitionBroadcastQueryWindow | null {
  if (!Number.isFinite(startAt) || !Number.isFinite(clientNow)) return null
  // A reconnect can miss the invalidation that carries the authoritative server
  // timestamp. Keep a small future margin so that a slow client clock does not
  // hide an already committed event until the user refreshes manually.
  const queryEnd = Math.max(
    clientNow + competitionBroadcastClockSkewMs,
    Number.isFinite(latestNotifiedAt) ? latestNotifiedAt : 0,
  )
  return {
    from: new Date(Math.max(startAt, queryEnd - competitionBroadcastLookbackMs)).toISOString(),
    to: new Date(queryEnd).toISOString(),
  }
}

const competitionBroadcastKindSet: ReadonlySet<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol>
  = new Set(competitionBroadcastKinds)

export function isCompetitionBroadcastKind(
  kind: NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
): boolean {
  return competitionBroadcastKindSet.has(kind)
}

export function competitionBroadcastIdentity(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string {
  return [
    event.kind,
    event.teamId,
    event.competitionChallengeId,
    event.occurredAt,
  ].join(':')
}

export function deduplicateCompetitionBroadcasts(
  events: readonly NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[],
): NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[] {
  const seen = new Set<string>()
  return events.filter((event) => {
    const key = competitionBroadcastIdentity(event)
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
}

export function mergeCompetitionBroadcasts(
  current: readonly NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[],
  incoming: readonly NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[],
): NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse[] {
  const currentByIdentity = new Map(current.map(event => [
    competitionBroadcastIdentity(event),
    event,
  ]))
  return deduplicateCompetitionBroadcasts(incoming).map(event =>
    currentByIdentity.get(competitionBroadcastIdentity(event)) ?? event)
}

export function competitionBroadcastText(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string {
  const team = event.teamDisplayName ?? translate("common.label.team.competitionBroadcast")
  const challenge = event.challengeTitle ?? translate("common.label.challenge")
  if (!event.kind) return translate("competitions.label.competitionStatusUpdated")
  const awdpSucceeded = event.gameplayFactState === 'Completed'
    && event.gameplayFactResult === 'Correct'
  const messages: Partial<Record<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
    FirstBloodAwarded: translate("competitions.competitionBroadcast.label.teamEarnedFirstBlood", { team, challenge }),
    SecondBloodAwarded: translate("competitions.competitionBroadcast.label.teamEarnedSecondBlood", { team, challenge }),
    ThirdBloodAwarded: translate("competitions.competitionBroadcast.label.teamEarnedThirdBlood", { team, challenge }),
    TeamBanned: translate("competitions.competitionBroadcast.label.teamWasBannedCheating", { team }),
    TeamBanCorrectionPublished: translate("competitions.competitionBroadcast.description.teamWonAppealBan", { team }),
    HintPublished: translate("competitions.competitionBroadcast.label.challengeNewHint", { challenge }),
    ChallengeDescriptionUpdated: translate("common.label.challengeUpdatedDescription", { challenge }),
    ChallengePublished: translate("competitions.label.challengeNowOpen", { challenge }),
    AwdpBreakResolved: awdpSucceeded
      ? translate("competitions.label.teamSuccessfullyAttackedChallenge", { team, challenge })
      : translate("competitions.competitionBroadcast.error.teamAttackChallengeFailed", { team, challenge }),
    AwdpFixResolved: awdpSucceeded
      ? translate("competitions.label.teamSuccessfullyDefendedChallenge", { team, challenge })
      : translate("competitions.competitionBroadcast.error.teamDefendChallengeFailed", { team, challenge }),
    AnnouncementPublished: translate("competitions.competitionBroadcast.description.newCompetitionNoticeWas"),
  }
  return messages[event.kind] ?? translate("competitions.label.competitionStatusUpdated")
}

export function competitionBroadcastTargetPath(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string | null {
  if (event.competitionChallengeId) {
    return competitionChallengePath(event.competitionId!, event.competitionChallengeId)
  }
  if (event.kind === 'TeamBanned' || event.kind === 'TeamBanCorrectionPublished')
    return competitionTeamsPath(event.competitionId!)
  if (event.kind === 'AnnouncementPublished' && event.questionId)
    return `/notifications?notification=${event.questionId}`
  return null
}
