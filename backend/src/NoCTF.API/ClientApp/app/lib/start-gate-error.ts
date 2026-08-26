import type { NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse } from '~/api'
import { localizeMessage, translate } from '../utils/i18n'

export function startGateErrorMessage(
  error: Pick<NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse, 'code' | 'message'>,
): string {
  const message = error.message ?? ''
  const isSchemaVersionFailure = /schema\s*version/i.test(message)
    && /unsupported|supported versions/i.test(message)

  if (isSchemaVersionFailure) {
    switch (error.code) {
      case 'CompetitionConfigurationInvalid':
        return translate('比赛模式配置版本过旧，请重新保存比赛配置。')
      case 'RuntimeDefinitionInvalid':
        return translate('题目运行环境定义版本过旧，请进入题目模板重新保存题目定义。')
      case 'ChallengeRulesInvalid':
        return translate('题目规则版本过旧，请重新保存题目规则。')
    }
  }

  switch (error.code) {
    case 'CompetitionNotPublished':
      return translate('比赛必须处于已发布状态才能执行启动前检查。')
    case 'CompetitionConfigurationInvalid':
      return translate('比赛模式配置无效，请检查比赛配置。')
    case 'PublishedChallengeRequired':
      return translate('至少需要发布一道题目。')
    case 'ApprovedTeamRequired':
      return translate('至少需要一支审核通过的队伍。')
    case 'RuntimeQuotaInsufficient':
      return translate('每队并发运行环境上限不足以承载全部已发布的 AWD 题目。')
    case 'ChallengeModeMismatch':
      return translate('题目模式与比赛模式不一致。')
    case 'ChallengeRulesInvalid':
      return translate('题目规则无效，请检查比赛题目配置。')
    case 'RuntimeDefinitionInvalid':
      return translate('题目运行环境定义无效，请检查题目模板。')
    case 'TrackConfigurationInvalid':
      return translate('赛道配置无效，请检查赛道设置。')
    case 'TeamTrackInvalid':
      return translate('存在队伍使用了已不存在的赛道，请调整队伍赛道。')
    default:
      return localizeMessage(message)
  }
}
