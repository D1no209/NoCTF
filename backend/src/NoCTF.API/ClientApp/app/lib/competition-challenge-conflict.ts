import type {
  NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictCode,
  NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictResponse,
} from '../api'
import { translate } from '../utils/i18n'

function readConflictCode(error: unknown): NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictCode | undefined {
  if (!error || typeof error !== 'object' || !('code' in error))
    return undefined

  switch (error.code) {
    case 'ChallengeTemplateConflict':
    case 'ChallengeOrderConflict':
    case 'ResourceIdConflict':
    case 'RevisionConflict':
    case 'LifecycleStateConflict':
    case 'ChallengeTemplateNotFound':
    case 'ChallengeTemplateModeMismatch':
      return error.code
    default:
      return undefined
  }
}

export function competitionChallengeConflictMessage(error: unknown): string | undefined {
  const code: NoCtfapiEndpointsAdministrationChallengesCompetitionChallengeConflictResponse['code'] | undefined
    = readConflictCode(error)

  switch (code) {
    case 'ChallengeTemplateConflict':
      return translate('该题目已加入当前比赛,请编辑已有题目。')
    case 'ChallengeOrderConflict':
      return translate('该顺序已被其他题目占用,请更换顺序。')
    case 'ResourceIdConflict':
      return translate('题目资源标识冲突,请重新添加。')
    case 'RevisionConflict':
      return translate('题目已被其他人修改,请刷新后重试。')
    default:
      return undefined
  }
}
