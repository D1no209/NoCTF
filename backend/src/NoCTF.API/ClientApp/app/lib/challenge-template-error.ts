import type { UiMessage } from '../utils/i18n'
import { parseApiError } from '../utils/api-error'
import type { DefinitionModel } from '../utils/game-config'
import type { ChallengeTemplateSection } from './challenge-template-validation'

export function challengeTemplateWriteErrorSection(error: unknown, definition: DefinitionModel): ChallengeTemplateSection | null {
  const fields = Object.keys(parseApiError(error).fieldMessages ?? {})
  for (const field of fields) {
    const path = field.toLowerCase()
    if (/(^|\.)(title|visibility|direction|description)$/.test(path)) return 'basic'
    if (/(^|\.)runtime(\.|$)/.test(path)) return 'runtime'
    const index = /capabilities\[(\d+)\]/.exec(path)?.[1]
    const capability = index === undefined ? null : definition.capabilities[Number(index)]
    switch (capability) {
      case 'Break': return 'flags'
      case 'Fix': return 'fix'
      case 'Check': return 'check'
      case 'Control': return 'control'
      case 'Questionnaire': return 'questionnaire'
    }
  }
  return null
}

/** The API publishes semantic message descriptors, including each validation field. */
export function challengeTemplateWriteErrorMessages(error: unknown): UiMessage[] {
  const parsed = parseApiError(error)
  const fields = Object.values(parsed.fieldMessages ?? {}).flat()
  return fields.length ? fields : [parsed.displayMessage]
}

export function challengeTemplateWriteErrorMessage(error: unknown): UiMessage {
  return { messages: challengeTemplateWriteErrorMessages(error), separator: 'common.validation.separator' }
}
