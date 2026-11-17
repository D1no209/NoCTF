import { isMessageKey, message } from '../../../utils/i18n'
import type { UiMessage } from '../../../utils/i18n'
import type { NoCtfapiEndpointsAdministrationCompetitionsCredentialsImportCompetitionCredentialsResponse } from '../../../api'

export function credentialImportFeedback(response: NoCtfapiEndpointsAdministrationCompetitionsCredentialsImportCompetitionCredentialsResponse): {
  error: UiMessage
  issues: { rowNumber: number; description: UiMessage }[]
} {
  const failureKey = `administration.credentials.failure.${response.code}`
  return {
    error: message(isMessageKey(failureKey) ? failureKey : 'administration.credentials.importFailed'),
    issues: (response.issues ?? []).map(issue => {
      const issueKey = `administration.credentials.issue.${issue.code}`
      return {
        rowNumber: issue.rowNumber ?? 0,
        description: message(isMessageKey(issueKey) ? issueKey : 'administration.credentials.issue.InvalidValue', {
          value: issue.message ?? '',
        }),
      }
    }),
  }
}
