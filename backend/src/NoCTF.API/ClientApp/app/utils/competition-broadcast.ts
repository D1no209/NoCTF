import type {
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
} from '~/api'
import { translate } from './i18n'

export const competitionBroadcastKinds = [
  'FirstBloodAwarded',
  'SecondBloodAwarded',
  'ThirdBloodAwarded',
  'TeamBanned',
  'TeamBanCorrectionPublished',
  'HintPublished',
  'ChallengeDescriptionUpdated',
  'ChallengePublished',
] satisfies NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol[]

export function competitionBroadcastText(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string {
  const team = event.teamDisplayName ?? translate('某队伍')
  const challenge = event.challengeTitle ?? translate('某题目')
  if (!event.kind) return translate("赛事状态已更新")
  const messages: Partial<Record<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
    FirstBloodAwarded: translate('队伍「{team}」获得题目「{challenge}」一血', { team, challenge }),
    SecondBloodAwarded: translate('队伍「{team}」获得题目「{challenge}」二血', { team, challenge }),
    ThirdBloodAwarded: translate('队伍「{team}」获得题目「{challenge}」三血', { team, challenge }),
    TeamBanned: translate('队伍「{team}」由于「作弊」被封禁', { team }),
    TeamBanCorrectionPublished: translate('队伍「{team}」申诉成功，封禁已撤销', { team }),
    HintPublished: translate('题目「{challenge}」发布了新的提示', { challenge }),
    ChallengeDescriptionUpdated: translate('题目「{challenge}」已更新描述', { challenge }),
    ChallengePublished: translate('题目「{challenge}」已开放', { challenge }),
  }
  return messages[event.kind] ?? translate('赛事状态已更新')
}

export function competitionBroadcastTargetPath(
  event: NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
): string | null {
  if (event.competitionChallengeId) {
    return `/competitions/${event.competitionId}/challenges/${event.competitionChallengeId}`
  }
  if (event.kind === 'TeamBanned' || event.kind === 'TeamBanCorrectionPublished')
    return `/competitions/${event.competitionId}/teams`
  return null
}
