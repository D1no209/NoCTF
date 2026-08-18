import { localizeMessage, translate } from './i18n'

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
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}

/** Convert an SDK error payload into a user-facing ApiError. */
export function parseApiError(error: unknown, fallback = translate("请求失败,请稍后重试")): ApiError {
  if (error instanceof ApiError) return error
  if (error && typeof error === 'object') {
    const problem = error as ProblemDetailsLike
    const firstFieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined
    // 字段级校验错误(FluentValidation)优先于泛泛的 title("One or more validation errors occurred")。
    const message = localizeMessage(problem.detail ?? firstFieldError ?? problem.title ?? fallback)
    return new ApiError(message, {
      status: problem.status,
      code: problem.code,
      fieldErrors: problem.errors,
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
    case 409: return translate("请求与当前状态冲突,请检查后重试")
    case 413: return translate("上传的文件过大")
    case 429: return translate("请求过于频繁,请稍后重试")
    default:
      if (status !== undefined && status >= 500) return translate("服务器内部错误,请稍后重试")
      if (status === undefined) return translate("无法连接到服务器,请检查网络后重试")
      return translate("请求失败,请稍后重试")
  }
}
