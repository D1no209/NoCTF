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
    return translate("ui.theAppealStatementMustBeAtLeastCharactersItCurrently", { minimum: minimumAppealStatementLength, length })
  if (length > maximumAppealStatementLength)
    return translate("ui.theAppealStatementCannotExceedCharacters", { maximum: maximumAppealStatementLength })
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
    return translate("ui.forTopicRelatedInquiriesYouMustSelectARelatedTopic")

  const titleLength = draft.title.trim().length
  if (titleLength < minimumQuestionTitleLength)
    return translate("ui.theTitleMustBeAtLeastCharactersItCurrentlyHas", { minimum: minimumQuestionTitleLength, length: titleLength })
  if (titleLength > maximumQuestionTitleLength)
    return translate("ui.theTitleCannotExceedCharacters", { maximum: maximumQuestionTitleLength })

  const bodyLength = draft.body.trim().length
  if (bodyLength < minimumQuestionBodyLength)
    return translate("ui.theContentMustBeAtLeastCharactersItCurrentlyHas", { minimum: minimumQuestionBodyLength, length: bodyLength })
  if (bodyLength > maximumQuestionBodyLength)
    return translate("ui.theContentCannotExceedCharacters", { maximum: maximumQuestionBodyLength })

  return null
}
