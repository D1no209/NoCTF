export const minimumAppealStatementLength = 16
export const maximumAppealStatementLength = 512
export const minimumQuestionTitleLength = 4
export const maximumQuestionTitleLength = 160
export const minimumQuestionBodyLength = 4
export const maximumQuestionBodyLength = 4000

export function validateAppealStatement(value: string): string | null {
  const length = value.trim().length
  if (length < minimumAppealStatementLength)
    return `申诉陈述至少需要 ${minimumAppealStatementLength} 个字符，当前为 ${length} 个字符。`
  if (length > maximumAppealStatementLength)
    return `申诉陈述不能超过 ${maximumAppealStatementLength} 个字符。`
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
    return '题目相关咨询必须选择一个关联题目。'

  const titleLength = draft.title.trim().length
  if (titleLength < minimumQuestionTitleLength)
    return `标题至少需要 ${minimumQuestionTitleLength} 个字符，当前为 ${titleLength} 个字符。`
  if (titleLength > maximumQuestionTitleLength)
    return `标题不能超过 ${maximumQuestionTitleLength} 个字符。`

  const bodyLength = draft.body.trim().length
  if (bodyLength < minimumQuestionBodyLength)
    return `内容至少需要 ${minimumQuestionBodyLength} 个字符，当前为 ${bodyLength} 个字符。`
  if (bodyLength > maximumQuestionBodyLength)
    return `内容不能超过 ${maximumQuestionBodyLength} 个字符。`

  return null
}
