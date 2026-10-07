import { client } from '../api/client.gen'
import { currentLocale } from '../utils/i18n'
import { requiresInteractiveAuthentication, shouldRefreshSession } from '../lib/auth-refresh'
import {
  getAccessToken,
  isImpersonatingSession,
  refreshSession,
  requestImpersonationEnd,
} from '../lib/session'
import { statusErrorMessage } from '../utils/api-error'
import { prepareCommandRequest, observeCommandResponse } from '../utils/command-attempt'

/**
 * Configure the generated hey-api client:
 * - inject the in-memory Bearer token into every request;
 * - on 401 for a request that carried the in-memory token, single-flight refresh then retry once;
 * - when an error response carries no problem+json body (e.g. bare 401/403),
 *   synthesize a status-based message so callers never see a bare「请求失败」.
 */
export default defineNuxtPlugin(() => {
  const auth = useAuth()
  client.interceptors.request.use(async (request, options) => {
    request.headers.set('Accept-Language', currentLocale())
    const token = getAccessToken()
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`)
    }
    await prepareCommandRequest(request, options.body)
    return request
  })

  client.interceptors.response.use(async (response, request, options) => {
    const problem = (response.status === 401 || response.status === 403) ? await response.clone().json().catch(() => null) as { code?: unknown } | null : null
    if ((response.status === 401 || response.status === 403) && requiresInteractiveAuthentication(problem?.code) && !request.url.includes('/auth/mfa/')) {
      auth.invalidate()
      await navigateTo('/auth/login')
      return response
    }
    if (!shouldRefreshSession(response.status, request.headers, request.url, problem?.code)) {
      if (response.ok) observeCommandResponse(request, response.status)
      return response
    }
    if (isImpersonatingSession()) {
      requestImpersonationEnd('unauthorized')
      return response
    }
    const refreshed = await refreshSession()
    if (!refreshed) {
      return response
    }
    const headers = new Headers(request.headers)
    headers.set('Authorization', `Bearer ${getAccessToken()}`)
    headers.set('Accept-Language', currentLocale())
    const body = (options.serializedBody as BodyInit | undefined) ?? (options.body as BodyInit | undefined)
    const retried = await fetch(
      new Request(request.url, {
        method: options.method ?? 'GET',
        headers,
        body: options.method === 'GET' || options.method === 'HEAD' ? undefined : body,
        credentials: 'same-origin',
      }),
    )
    if (retried.ok) observeCommandResponse(request, retried.status)
    return retried
  })

  client.interceptors.error.use((error, response, request) => {
    observeCommandResponse(request, response?.status, true)
    if (error instanceof Error && (error.name === 'TimeoutError' || error.name === 'AbortError'))
      return { status: 503, code: 'RequestTimeout', detail: translate("common.error.requestTimeout") }
    const status = response?.ok === false ? response.status : undefined
    // 保留真实的 problem+json 及强类型失败响应体。很多业务冲突只携带
    // code/message；丢弃它们会把明确原因退化成笼统的 HTTP 状态提示。
    if (error && typeof error === 'object' && !(error instanceof Error)) {
      const problem = error as Record<string, unknown>
      if (typeof problem.detail === 'string'
        || typeof problem.title === 'string'
        || typeof problem.message === 'string'
        || typeof problem.code === 'string'
        || problem.errors) {
        return status === undefined || typeof problem.status === 'number'
          ? error
          : { ...problem, status }
      }
    }
    // 空 400/422 保留为纯状态，让调用处的操作级 fallback 保持具体。
    // 其他空响应体仍合成状态说明；401 需要区分登录失败与会话过期。
    const authenticatedRequest = request?.headers.has('Authorization') ?? false
    return status === 400 || status === 422
      ? { status }
      : { status, detail: statusErrorMessage(status, authenticatedRequest) }
  })
})
