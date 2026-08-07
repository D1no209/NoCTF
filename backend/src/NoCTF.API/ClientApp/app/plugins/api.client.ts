import { client } from '~/api/client.gen'
import { getAccessToken, refreshSession } from '~/lib/session'

/**
 * Configure the generated hey-api client:
 * - inject the in-memory Bearer token into every request;
 * - on 401 (outside /auth/*), single-flight refresh then retry the request once.
 */
export default defineNuxtPlugin(() => {
  client.interceptors.request.use((request) => {
    const token = getAccessToken()
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`)
    }
    return request
  })

  client.interceptors.response.use(async (response, request, options) => {
    if (response.status !== 401 || request.url.includes('/api/v1/auth/')) {
      return response
    }
    const refreshed = await refreshSession()
    if (!refreshed) {
      return response
    }
    const headers = new Headers(request.headers)
    headers.set('Authorization', `Bearer ${getAccessToken()}`)
    const body = (options.serializedBody as BodyInit | undefined) ?? (options.body as BodyInit | undefined)
    return fetch(
      new Request(request.url, {
        method: options.method ?? 'GET',
        headers,
        body: options.method === 'GET' || options.method === 'HEAD' ? undefined : body,
        credentials: 'same-origin',
      }),
    )
  })
})
