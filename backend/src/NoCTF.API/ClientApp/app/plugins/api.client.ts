import { client } from '~/api/client.gen'
import { shouldRefreshSession } from '~/lib/auth-refresh'
import { getAccessToken, refreshSession } from '~/lib/session'
import { statusErrorMessage } from '~/utils/api-error'
import { prepareCommandRequest, observeCommandResponse } from '~/utils/command-attempt'

/**
 * Configure the generated hey-api client:
 * - inject the in-memory Bearer token into every request;
 * - on 401 for a request that carried the in-memory token, single-flight refresh then retry once;
 * - when an error response carries no problem+json body (e.g. bare 401/403),
 *   synthesize a status-based message so callers never see a bare「请求失败」.
 */
export default defineNuxtPlugin(() => {
  client.interceptors.request.use(async (request, options) => {
    const token = getAccessToken()
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`)
    }
    await prepareCommandRequest(request, options.body)
    return request
  })

  client.interceptors.response.use(async (response, request, options) => {
    if (!shouldRefreshSession(response.status, request.headers)) {
      if (response.ok) observeCommandResponse(request, response.status)
      return response
    }
    const refreshed = await refreshSession()
    if (!refreshed) {
      return response
    }
    const headers = new Headers(request.headers)
    headers.set('Authorization', `Bearer ${getAccessToken()}`)
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
      return { status: 503, code: 'RequestTimeout', detail: translate('请求超时，结果暂未确认，请重试。') }
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
    // 空响应体(如登录 401)或网络错误:合成带状态码的 problem 形状,
    // parseApiError 会读出其中的 detail 与 status。
    const authenticatedRequest = request?.headers.has('Authorization') ?? false
    return { status, detail: statusErrorMessage(status, authenticatedRequest) }
  })
})
