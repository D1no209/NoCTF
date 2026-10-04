import type { NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode } from '../api'
import { translate } from './i18n'

const messages: Record<NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode, string> = {
  Forbidden: "common.patch.description.competitionSOwnerManagers",
  SubmissionNotFound: "common.patch.description.submissionExistCompetition",
  NotFixSubmission: "common.patch.description.fixSubmissionsDownloadablePatch",
  PatchNotFound: "common.patch.description.submissionPatchArchive",
  InvalidAssociation: "common.patch.description.patchArchiveAssociationMatch",
  FileNotFound: "common.patch.description.patchArchiveFileLonger",
  StorageUnavailable: "common.patch.error.patchArchiveStorageUnavailable",
  AuditUnavailable: "common.patch.description.patchDownloadAuditCould",
}

export function adminPatchFailureMessage(code: NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode): string {
  return translate(messages[code])
}
