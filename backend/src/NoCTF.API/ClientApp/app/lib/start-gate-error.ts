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

  return localizeMessage(message)
}
