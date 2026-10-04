import type { NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeConflictCode, NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeConflictResponse } from '../api/models'
import { translate } from '../utils/i18n'

function readConflictCode(error: unknown): NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeConflictCode | undefined {
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

export function competitionChallengeConflictMessage(error: unknown): string | null | undefined {
  const code: NoCTFAPIEndpointsAdministrationChallengesCompetitionChallengeConflictResponse['code'] | undefined
    = readConflictCode(error)

  switch (code) {
    case 'ChallengeTemplateConflict':
      return translate("challenges.competitionChallenge.description.challengeAlreadyCompetitionEdit")
    case 'ChallengeOrderConflict':
      return translate("challenges.competitionChallenge.description.orderAlreadyAnotherChallenge")
    case 'ResourceIdConflict':
      return translate("challenges.competitionChallenge.description.challengeResourceIdentifierConflicts")
    default:
      return undefined
  }
}
