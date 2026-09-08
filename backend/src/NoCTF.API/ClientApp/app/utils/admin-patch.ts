import type { NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode } from '../api'
import { translate } from './i18n'

const messages: Record<NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode, string> = {
  Forbidden: "ui.onlyThisCompetitionSOwnerManagersJudgesOrAPlatform",
  SubmissionNotFound: "ui.theSubmissionDoesNotExistInThisCompetition",
  NotFixSubmission: "ui.onlyFixSubmissionsHaveADownloadablePatchArchive",
  PatchNotFound: "ui.thisSubmissionHasNoPatchArchive",
  InvalidAssociation: "ui.thePatchArchiveAssociationDoesNotMatchTheSubmissionS",
  FileNotFound: "ui.thePatchArchiveFileNoLongerExists",
  StorageUnavailable: "ui.patchArchiveStorageIsTemporarilyUnavailableTryAgainLater",
  AuditUnavailable: "ui.thePatchDownloadAuditCouldNotBeSavedNoFile",
}

export function adminPatchFailureMessage(code: NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode): string {
  return translate(messages[code])
}
