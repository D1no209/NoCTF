import { message as describeMessage } from './i18n'
import type { MessageDescriptor, UiMessage } from './i18n'
import { currentLocale, isMessageKey, localizeMessage, translate } from './i18n'

export class ApiError extends Error {
  readonly status?: number
  readonly code?: string
  readonly fieldErrors?: Record<string, string[]>
  readonly fieldMessages?: Record<string, UiMessage[]>
  readonly displayMessage: UiMessage

  constructor(value: UiMessage, init?: { status?: number; code?: string; fieldErrors?: Record<string, string[]>; fieldMessages?: Record<string, UiMessage[]> }) {
    super(localizeMessage(value))
    this.name = 'ApiError'
    this.displayMessage = value
    this.status = init?.status
    this.code = init?.code
    this.fieldErrors = init?.fieldErrors
    this.fieldMessages = init?.fieldMessages
    Object.defineProperty(this, 'message', { get: () => localizeMessage(this.displayMessage), configurable: true })
  }
}

interface ProblemDetailsLike {
  status?: number
  statusCode?: number
  title?: string
  detail?: string
  message?: string
  code?: string
  messageKey?: string
  messageArguments?: unknown
  errors?: Record<string, string[] | string>
  errorMessages?: Record<string, Array<{ key: string; arguments?: unknown }>>
}

function descriptor(key: string | undefined, arguments_: unknown): MessageDescriptor | undefined {
  if (!key || !isMessageKey(key)) return undefined
  const argumentsObject = arguments_ && typeof arguments_ === 'object' && !Array.isArray(arguments_)
    ? Object.fromEntries(Object.entries(arguments_).filter(([, value]) => value === null || ['string', 'number', 'boolean'].includes(typeof value))) as Record<string, string | number | boolean | null>
    : {}
  return describeMessage(key, argumentsObject)
}

export function userFacingErrorMessage(value: string | null | undefined, fallback: UiMessage = describeMessage('common.error.requestFailed'), allowUntranslated = false): string {
  const source = value?.trim()
  if (!source) return localizeMessage(fallback)
  return !allowUntranslated && currentLocale() === 'zh-CN' && /[A-Za-z]{3}/.test(source) && !/[\u3400-\u9FFF]/.test(source)
    ? localizeMessage(fallback) : source
}

function stableCodeMessage(code: string | undefined): MessageDescriptor | null {
  switch (code) {
    case 'HumanVerificationRequired': return describeMessage('security.verification.required')
    case 'HumanVerificationFailed': return describeMessage('security.verification.failed')
    case 'HumanVerificationUnavailable': return describeMessage('security.verification.unavailable')
    case 'HumanVerificationSecretInvalid': return describeMessage('security.verification.invalidSecret')
    case 'CapConfigurationInvalid': return describeMessage('common.error.capConfigurationValidationFailed')
    case 'CapProviderUnavailable': return describeMessage('common.error.capProviderValidationUnavailable')
    case 'CapWorkloadProviderNotCap': return describeMessage('administration.validation.capProviderFormat')
    case 'CapWorkloadDifficultyInvalid': return describeMessage('administration.error.capDifficultyInvalid')
    case 'CapWorkloadChallengeCountInvalid': return describeMessage('administration.error.capChallengeCountInvalid')
    case 'CapWorkloadManagementCredentialMissing': return describeMessage('administration.label.capManagementCredentialMissing')
    case 'CapWorkloadManagementCredentialInvalid': return describeMessage('administration.error.capManagementCredentialInvalid')
    case 'CapWorkloadSiteKeyNotFound': return describeMessage('administration.label.capSiteKeyFound')
    case 'CapWorkloadProviderUnavailable': return describeMessage('administration.error.capWorkloadConfigurationUnavailable')
    case 'CapWorkloadConfigurationNotApplied': return describeMessage('administration.label.capWorkloadConfigurationApplied')
    case 'EmailVerificationDisabled': return describeMessage('common.description.testEmailSentEmail')
    case 'WriteUpSubmissionDeadlinePassed': return describeMessage('writeUp.deadlinePassedShort')
    case 'RuntimeExtensionTooEarly': return describeMessage('runtime.extend.tooEarly')
    default: return null
  }
}

/** Field errors retain precedence; protocol descriptors identify messages, never English prose. */
export function parseApiError(error: unknown, fallback: UiMessage = describeMessage('common.error.requestFailed')): ApiError {
  if (error instanceof ApiError) return error
  if (!error || typeof error !== 'object') return new ApiError(fallback)
  const problem = error as ProblemDetailsLike
  const status = problem.status ?? problem.statusCode
  const fieldErrors = problem.errors ? Object.fromEntries(Object.entries(problem.errors).map(([field, entries]) => [field, Array.isArray(entries) ? entries : [entries]])) : undefined
  const fields = new Set([...Object.keys(fieldErrors ?? {}), ...Object.keys(problem.errorMessages ?? {})])
  const fieldMessages = Object.fromEntries([...fields].map((field) => {
    const descriptions = problem.errorMessages?.[field] ?? []
    const raw = fieldErrors?.[field] ?? []
    return [field, Array.from({ length: Math.max(descriptions.length, raw.length) }, (_, index) =>
      descriptor(descriptions[index]?.key, descriptions[index]?.arguments) ?? raw[index] ?? '')]
  }))
  const unique = new Map<string, UiMessage>()
  for (const value of Object.values(fieldMessages).flat())
    if (typeof value !== 'string' || value.trim()) unique.set(JSON.stringify(value), value)
  const validation = [...unique.values()]
  const statusFallback = status === undefined ? fallback
    : (status === 400 || status === 422) && localizeMessage(fallback) !== translate('common.error.requestFailed') ? fallback : statusErrorDescriptor(status)
  const structured = descriptor(problem.messageKey, problem.messageArguments) ?? stableCodeMessage(problem.code)
  const raw = problem.detail ?? problem.message ?? problem.title
  const display: UiMessage = validation.length
    ? { messages: validation, separator: 'common.validation.separator' }
    : structured ?? (userFacingErrorMessage(raw, statusFallback, status === 409 || status === 422 || (status === 400 && Boolean(problem.code))) === localizeMessage(statusFallback)
      ? statusFallback : raw?.trim() || statusFallback)
  return new ApiError(display, { status, code: problem.code, fieldErrors, fieldMessages })
}

export function statusErrorDescriptor(status: number | undefined, authenticatedRequest = false): MessageDescriptor {
  switch (status) {
    case 400: return describeMessage('common.error.invalidRequest')
    case 401: return describeMessage(authenticatedRequest ? 'auth.session.expired' : 'auth.login.invalidCredentials')
    case 403: return describeMessage('common.error.forbidden')
    case 404: return describeMessage('common.error.notFound')
    case 409: return describeMessage('common.error.stateConflict')
    case 413: return describeMessage('common.error.uploadTooLarge')
    case 429: return describeMessage('common.error.rateLimited')
    default: return describeMessage(status !== undefined && status >= 500 ? 'common.error.serverUnavailable' : status === undefined ? 'common.error.networkUnavailable' : 'common.error.requestFailed')
  }
}

export function statusErrorMessage(status: number | undefined, authenticatedRequest = false): string {
  return localizeMessage(statusErrorDescriptor(status, authenticatedRequest))
}
