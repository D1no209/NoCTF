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
  fallback = translate("请求失败,请稍后重试"),
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

/** Convert an SDK error payload into a user-facing ApiError. */
export function parseApiError(error: unknown, fallback = translate("请求失败,请稍后重试")): ApiError {
  if (error instanceof ApiError) return error
  if (error && typeof error === 'object') {
    const problem = error as ProblemDetailsLike
    const status = problem.status ?? problem.statusCode
    const fieldErrors = normalizeFieldErrors(problem.errors)
    const firstFieldError = fieldErrors ? Object.values(fieldErrors).flat()[0] : undefined
    // 字段级校验错误(FluentValidation)优先于泛泛的 title("One or more validation errors occurred")。
    const statusFallback = status === undefined
      ? fallback
      : statusErrorMessage(status)
    const message = userFacingErrorMessage(
      problem.detail ?? firstFieldError ?? problem.message ?? problem.title,
      statusFallback,
      status === 409 || status === 422,
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
    case 400: return translate("请求参数有误,请检查输入")
    case 401:
      return authenticatedRequest ? translate("登录状态已失效,请重新登录") : translate("用户名或密码错误")
    case 403: return translate("没有权限执行此操作")
    case 404: return translate("请求的资源不存在")
    case 409: return translate("资源状态已发生变化,请刷新页面获取最新状态后重试")
    case 413: return translate("上传的文件过大")
    case 429: return translate("请求过于频繁,请稍后重试")
    default:
      if (status !== undefined && status >= 500) return translate("服务器内部错误,请稍后重试")
      if (status === undefined) return translate("无法连接到服务器,请检查网络后重试")
      return translate("请求失败,请稍后重试")
  }
}
