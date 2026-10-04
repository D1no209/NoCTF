import type { UiMessage } from '../utils/i18n'
import { parseApiError } from '../utils/api-error'

/** The API publishes semantic message descriptors, including each validation field. */
export function challengeTemplateWriteErrorMessages(error: unknown): UiMessage[] {
  const parsed = parseApiError(error)
  const fields = Object.values(parsed.fieldMessages ?? {}).flat()
  return fields.length ? fields : [parsed.displayMessage]
}

export function challengeTemplateWriteErrorMessage(error: unknown): UiMessage {
  return { messages: challengeTemplateWriteErrorMessages(error), separator: 'common.validation.separator' }
}
