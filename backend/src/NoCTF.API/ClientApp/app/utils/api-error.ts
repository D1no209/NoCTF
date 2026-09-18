import { currentLocale, localizeMessage, translate } from './i18n'

/**
 * Normalized API error parsed from RFC 9457 problem+json responses.
 * `code` is the stable machine code emitted by the backend (when present).
 */
export class ApiError extends Error {
  readonly status?: number
  readonly code?: string
  /** Field-level validation errors keyed by field name, when provided. */
  readonly fieldErrors?: Record<string, string[]>

  constructor(message: string, init?: { status?: number; code?: string; fieldErrors?: Record<string, string[]> }) {
    super(message)
    this.name = 'ApiError'
    this.status = init?.status
    this.code = init?.code
    this.fieldErrors = init?.fieldErrors
  }
}

interface ProblemDetailsLike {
  status?: number
  statusCode?: number
  title?: string
  detail?: string
  message?: string
  code?: string
  errors?: Record<string, string[] | string>
}

function isUntranslatedEnglish(message: string): boolean {
  return currentLocale() === 'zh-CN'
    && /[A-Za-z]{3}/.test(message)
    && !/[\u3400-\u9FFF]/.test(message)
}

/**
 * Localize backend text without leaking raw English diagnostics into the Chinese UI.
 * Known backend messages keep their explicit translation; unknown diagnostics fall
 * back to the caller's stable user-facing description.
 */
export function userFacingErrorMessage(
  message: string | null | undefined,
  fallback = translate("ui.requestFailedPleaseTryAgainLater"),
  allowUntranslated = false,
): string {
  const source = message?.trim()
  if (!source) return fallback

  const localized = localizeMessage(source)
  return !allowUntranslated && isUntranslatedEnglish(localized) ? fallback : localized
}

function normalizeFieldErrors(
  errors: ProblemDetailsLike['errors'],
): Record<string, string[]> | undefined {
  if (!errors) return undefined
  return Object.fromEntries(Object.entries(errors).map(([field, messages]) => [
    field,
    Array.isArray(messages) ? messages : [messages],
  ]))
}

function validationErrorMessage(fieldErrors: Record<string, string[]> | undefined): string | undefined {
  if (!fieldErrors) return undefined
  const messages = [...new Set(Object.values(fieldErrors)
    .flat()
    .map(message => message.trim())
    .filter(Boolean))]
  if (!messages.length) return undefined
  const localized = messages.map(message => userFacingErrorMessage(message, message, true))
  return localized
    .map((message, index) => index === localized.length - 1
      ? message
      : message.replace(/[。.!?；;]+$/u, ''))
    .join(translate('ui.validationErrorSeparator'))
}

function stableCodeMessage(code: string | undefined): string | null {
  switch (code) {
    case 'HumanVerificationRequired': return translate('ui.completeHumanVerificationBeforeRetrying')
    case 'HumanVerificationFailed': return translate('ui.humanVerificationFailedPleaseRetry')
    case 'HumanVerificationUnavailable': return translate('ui.humanVerificationProviderUnavailablePleaseRetry')
    case 'HumanVerificationSecretInvalid': return translate('ui.humanVerificationSecretInvalid')
    case 'CapWorkloadProviderNotCap': return translate('ui.capProviderMustBeSelected')
    case 'CapWorkloadDifficultyInvalid': return translate('ui.capDifficultyInvalid')
    case 'CapWorkloadChallengeCountInvalid': return translate('ui.capChallengeCountInvalid')
    case 'CapWorkloadManagementCredentialMissing': return translate('ui.capManagementCredentialMissing')
    case 'CapWorkloadManagementCredentialInvalid': return translate('ui.capManagementCredentialInvalid')
    case 'CapWorkloadSiteKeyNotFound': return translate('ui.capSiteKeyNotFound')
    case 'CapWorkloadProviderUnavailable': return translate('ui.capWorkloadConfigurationUnavailable')
    case 'CapWorkloadConfigurationNotApplied': return translate('ui.capWorkloadConfigurationNotApplied')
    case 'EmailVerificationDisabled': return translate('ui.theTestEmailWillBeSentToTheEmailAddress')
    case 'WriteUpSubmissionDeadlinePassed': return translate('writeUp.deadlinePassedShort')
    default: return null
  }
}

/** Convert an SDK error payload into a user-facing ApiError. */
export function parseApiError(error: unknown, fallback = translate("ui.requestFailedPleaseTryAgainLater")): ApiError {
  if (error instanceof ApiError) return error
  if (error && typeof error === 'object') {
    const problem = error as ProblemDetailsLike
    const status = problem.status ?? problem.statusCode
    const fieldErrors = normalizeFieldErrors(problem.errors)
    const validationMessage = validationErrorMessage(fieldErrors)
    const defaultFallback = translate('ui.requestFailedPleaseTryAgainLater')
    // 字段级校验错误(FluentValidation)优先于泛泛的 detail/title。
    const statusFallback = status === undefined
      ? fallback
      : (status === 400 || status === 422) && fallback !== defaultFallback
          ? fallback
          : statusErrorMessage(status)
    const message = stableCodeMessage(problem.code) ?? userFacingErrorMessage(
        validationMessage ?? problem.detail ?? problem.message ?? problem.title,
        statusFallback,
        status === 409 || status === 422 || Boolean(validationMessage) || (status === 400 && Boolean(problem.code)),
      )
    return new ApiError(message, {
      status,
      code: problem.code,
      fieldErrors,
    })
  }
  return new ApiError(fallback)
}

/** 无 problem+json 响应体时,按 HTTP 状态码给出有意义的提示。 */
export function statusErrorMessage(status: number | undefined, authenticatedRequest = false): string {
  switch (status) {
    case 400: return translate("ui.theRequestParametersAreIncorrectPleaseCheckYourInput")
    case 401:
      return authenticatedRequest ? translate("ui.loginStatusHasExpiredPleaseLogInAgain") : translate("ui.wrongUsernameOrPassword")
    case 403: return translate("ui.noPermissionToPerformThisOperation")
    case 404: return translate("ui.theRequestedResourceDoesNotExist")
    case 409: return translate("ui.theResourceStateChangedRefreshThePageToLoadThe")
    case 413: return translate("ui.theUploadedFileIsTooLarge")
    case 429: return translate("ui.theRequestIsTooFrequentPleaseTryAgainLater")
    default:
      if (status !== undefined && status >= 500) return translate("ui.internalServerErrorPleaseTryAgainLater")
      if (status === undefined) return translate("ui.unableToConnectToTheServerPleaseCheckTheNetwork")
      return translate("ui.requestFailedPleaseTryAgainLater")
  }
}
