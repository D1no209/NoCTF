import type { NoCTFAPIEndpointsAdministrationGameplayFactsAdminPatchFailureCode } from '../api/models'
import { translate } from './i18n'

const messages: Record<NoCTFAPIEndpointsAdministrationGameplayFactsAdminPatchFailureCode, string> = {
  Forbidden: "common.patch.description.competitionSOwnerManagers",
  SubmissionNotFound: "common.patch.description.submissionExistCompetition",
  NotFixSubmission: "common.patch.description.fixSubmissionsDownloadablePatch",
  PatchNotFound: "common.patch.description.submissionPatchArchive",
  InvalidAssociation: "common.patch.description.patchArchiveAssociationMatch",
  FileNotFound: "common.patch.description.patchArchiveFileLonger",
  StorageUnavailable: "common.patch.error.patchArchiveStorageUnavailable",
  AuditUnavailable: "common.patch.description.patchDownloadAuditCould",
}

export function adminPatchFailureMessage(code: NoCTFAPIEndpointsAdministrationGameplayFactsAdminPatchFailureCode): string {
  return translate(messages[code])
}
