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
    return translate('申诉陈述至少需要 {minimum} 个字符，当前为 {length} 个字符。', { minimum: minimumAppealStatementLength, length })
  if (length > maximumAppealStatementLength)
    return translate('申诉陈述不能超过 {maximum} 个字符。', { maximum: maximumAppealStatementLength })
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
    return translate("题目相关咨询必须选择一个关联题目。")

  const titleLength = draft.title.trim().length
  if (titleLength < minimumQuestionTitleLength)
    return translate('标题至少需要 {minimum} 个字符，当前为 {length} 个字符。', { minimum: minimumQuestionTitleLength, length: titleLength })
  if (titleLength > maximumQuestionTitleLength)
    return translate('标题不能超过 {maximum} 个字符。', { maximum: maximumQuestionTitleLength })

  const bodyLength = draft.body.trim().length
  if (bodyLength < minimumQuestionBodyLength)
    return translate('内容至少需要 {minimum} 个字符，当前为 {length} 个字符。', { minimum: minimumQuestionBodyLength, length: bodyLength })
  if (bodyLength > maximumQuestionBodyLength)
    return translate('内容不能超过 {maximum} 个字符。', { maximum: maximumQuestionBodyLength })

  return null
}
