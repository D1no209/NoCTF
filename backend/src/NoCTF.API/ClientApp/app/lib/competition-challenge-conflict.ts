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
      return translate("ui.thisChallengeIsAlreadyInTheCompetitionEditTheExisting")
    case 'ChallengeOrderConflict':
      return translate("ui.thisOrderIsAlreadyUsedByAnotherChallengeChooseA")
    case 'ResourceIdConflict':
      return translate("ui.theChallengeResourceIdentifierConflictsWithAnExistingResourceAdd")
    default:
      return undefined
  }
}
