import type { NoCtfApplicationLiveSoloRoundsLiveSoloFailure } from '../../api'
import type { MessageKey } from '../../locales/en'
import { ApiError, parseApiError } from '../../utils/api-error'
import { message, type UiMessage } from '../../utils/i18n'

const failureMessages = {
  NotFound: 'liveSolo.error.notFound', Forbidden: 'liveSolo.error.forbidden', Disabled: 'liveSolo.disabled',
  InvalidConfiguration: 'liveSolo.error.configuration', Conflict: 'liveSolo.error.conflict', RosterLocked: 'liveSolo.rosterLocked',
  NotReady: 'liveSolo.error.notReady', MediaUnavailable: 'liveSolo.error.mediaUnavailable', IsolationUnavailable: 'liveSolo.error.isolation',
  RoundNotRunning: 'liveSolo.error.roundNotRunning', RoundPaused: 'liveSolo.state.paused', QuestionNotOpen: 'liveSolo.error.questionNotOpen',
  DeadlinePassed: 'liveSolo.error.deadline', AlreadyEnded: 'liveSolo.state.completed', NoSuitableQuestionGroup: 'liveSolo.error.questionGroup',
  DependencyUnavailable: 'liveSolo.error.dependency', IdempotencyConflict: 'liveSolo.error.idempotency',
} satisfies Record<NoCtfApplicationLiveSoloRoundsLiveSoloFailure, MessageKey>

export function parseLiveSoloError(cause: unknown, fallback: UiMessage): ApiError {
  const parsed = parseApiError(cause, fallback)
  if (Object.keys(parsed.fieldErrors ?? {}).length || Object.keys(parsed.fieldMessages ?? {}).length || !parsed.code
    || !Object.hasOwn(failureMessages, parsed.code)) return parsed
  const key = (failureMessages as Record<string, MessageKey>)[parsed.code]
  return key ? new ApiError(message(key), { status: parsed.status, code: parsed.code }) : parsed
}
