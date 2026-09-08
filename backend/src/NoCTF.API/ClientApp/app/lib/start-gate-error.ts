import type { NoCtfapiEndpointsAdministrationCompetitionsStartGateErrorResponse } from '../api'
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
        return translate("ui.theCompetitionModeConfigurationIsOutdatedSaveTheCompetitionConfiguration")
      case 'RuntimeDefinitionInvalid':
        return translate("ui.theChallengeRuntimeDefinitionIsOutdatedOpenTheChallengeTemplate")
      case 'ChallengeRulesInvalid':
        return translate("ui.theChallengeRulesAreOutdatedSaveTheChallengeRulesAgain")
    }
  }

  switch (error.code) {
    case 'CompetitionNotPublished':
      return translate("ui.theCompetitionMustBePublishedBeforeStartValidationIsAvailable")
    case 'CompetitionConfigurationInvalid':
      return translate("ui.theCompetitionModeConfigurationIsInvalidCheckTheCompetitionSettings")
    case 'PublishedChallengeRequired':
      return translate("ui.publishAtLeastOneChallenge")
    case 'ApprovedTeamRequired':
      return translate("ui.atLeastOneApprovedTeamIsRequired")
    case 'RuntimeQuotaInsufficient':
      return translate("ui.thePerTeamRuntimeLimitIsTooLowForAll")
    case 'ChallengeModeMismatch':
      return translate("ui.theChallengeModeDoesNotMatchTheCompetitionMode")
    case 'ChallengeRulesInvalid':
      return translate("ui.theChallengeRulesAreInvalidCheckTheCompetitionChallengeSettings")
    case 'RuntimeDefinitionInvalid':
      return translate("ui.theChallengeRuntimeDefinitionIsInvalidCheckTheChallengeTemplate")
    case 'TrackConfigurationInvalid':
      return translate("ui.theTrackConfigurationIsInvalidCheckTheTrackSettings")
    case 'TeamTrackInvalid':
      return translate("ui.aTeamReferencesATrackThatNoLongerExistsReassign")
    default:
      return localizeMessage(message)
  }
}
