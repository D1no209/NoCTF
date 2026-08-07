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
export function parseApiError(error: unknown, fallback = '请求失败,请稍后重试'): ApiError {
  if (error instanceof ApiError) return error
  if (error && typeof error === 'object') {
    const problem = error as ProblemDetailsLike
    const firstFieldError = problem.errors ? Object.values(problem.errors).flat()[0] : undefined
    const message = problem.detail ?? problem.title ?? firstFieldError ?? fallback
    return new ApiError(message, {
      status: problem.status,
      code: problem.code,
      fieldErrors: problem.errors,
    })
  }
  return new ApiError(fallback)
}
