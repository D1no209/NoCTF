import { translate } from '../utils/i18n'

export const minimumAppealStatementLength = 16
export const maximumAppealStatementLength = 512
export const minimumQuestionTitleLength = 4
export const maximumQuestionTitleLength = 160
export const minimumQuestionBodyLength = 4
export const maximumQuestionBodyLength = 4000

export function validateAppealStatement(value: string): string | null {
  const length = value.trim().length
  if (length < minimumAppealStatementLength)
    return translate("common.participantValidation.validation.appealStatementLength.participantFormValidation", { minimum: minimumAppealStatementLength, length })
  if (length > maximumAppealStatementLength)
    return translate("common.participantValidation.validation.appealStatementLength", { maximum: maximumAppealStatementLength })
  return null
}

export interface CompetitionQuestionDraft {
  requiresChallenge: boolean
  challengeId: string | null
  title: string
  body: string
}

export function validateCompetitionQuestionDraft(
  draft: CompetitionQuestionDraft,
): string | null {
  if (draft.requiresChallenge && !draft.challengeId)
    return translate("common.participantValidation.validation.topicRelatedFormat")

  const titleLength = draft.title.trim().length
  if (titleLength < minimumQuestionTitleLength)
    return translate("common.participantValidation.validation.titleLeastLength", { minimum: minimumQuestionTitleLength, length: titleLength })
  if (titleLength > maximumQuestionTitleLength)
    return translate("common.participantValidation.validation.titleExceedLength", { maximum: maximumQuestionTitleLength })

  const bodyLength = draft.body.trim().length
  if (bodyLength < minimumQuestionBodyLength)
    return translate("common.participantValidation.validation.contentLeastLength", { minimum: minimumQuestionBodyLength, length: bodyLength })
  if (bodyLength > maximumQuestionBodyLength)
    return translate("common.participantValidation.validation.contentExceedLength", { maximum: maximumQuestionBodyLength })

  return null
}
