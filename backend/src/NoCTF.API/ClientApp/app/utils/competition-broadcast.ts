import type {
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
  NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse,
} from '~/api'

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
  const team = event.teamDisplayName ? `「${event.teamDisplayName}」` : '某队伍'
  const challenge = event.challengeTitle ? `「${event.challengeTitle}」` : '某题目'
  if (!event.kind) return '赛事状态已更新'
  const messages: Partial<Record<NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, string>> = {
    FirstBloodAwarded: `队伍${team}获得题目${challenge}一血`,
    SecondBloodAwarded: `队伍${team}获得题目${challenge}二血`,
    ThirdBloodAwarded: `队伍${team}获得题目${challenge}三血`,
    TeamBanned: `队伍${team}由于「作弊」被封禁`,
    TeamBanCorrectionPublished: `队伍${team}申诉成功，封禁已撤销`,
    HintPublished: `题目${challenge}发布了新的提示`,
    ChallengeDescriptionUpdated: `题目${challenge}已更新描述`,
    ChallengePublished: `题目${challenge}已开放`,
  }
  return messages[event.kind] ?? '赛事状态已更新'
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
