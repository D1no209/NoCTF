import type { NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse } from '../api'
import { localizeMessage, translate } from '../utils/i18n'

export function startGateErrorMessage(
  error: Pick<NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse, 'code' | 'message'>,
): string {
  const message = error.message ?? ''
  switch (error.code) {
    case 'CompetitionNotPublished':
      return translate("common.startGate.validation.competitionPublishedFormat")
    case 'CompetitionConfigurationInvalid':
      return translate("common.startGate.error.competitionModeConfigurationInvalid")
    case 'PublishedChallengeRequired':
      return translate("common.startGate.label.publishLeastOneChallenge")
    case 'ApprovedTeamRequired':
      return translate("common.startGate.validation.leastOneRequired")
    case 'RuntimeQuotaInsufficient':
      return translate("common.startGate.description.teamRuntimeLimitToo")
    case 'ChallengeModeMismatch':
      return translate("common.startGate.description.challengeModeMatchCompetition")
    case 'ChallengeRulesInvalid':
      return translate("common.startGate.error.challengeRulesCheckInvalid")
    case 'RuntimeDefinitionInvalid':
      return translate("common.startGate.error.challengeRuntimeDefinitionInvalid")
    case 'TrackConfigurationInvalid':
      return translate("common.startGate.error.trackConfigurationCheckInvalid")
    case 'TeamTrackInvalid':
      return translate("common.startGate.description.teamReferencesTrackLonger")
    default:
      return localizeMessage(message)
  }
}
