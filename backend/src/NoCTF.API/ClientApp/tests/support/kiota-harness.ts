import { AnonymousAuthenticationProvider, type RequestOption } from '@microsoft/kiota-abstractions'
import { FetchRequestAdapter, HttpClient, type Middleware } from '@microsoft/kiota-http-fetchlibrary'
import { createNoCtfClient } from '../../app/api/noCtfClient'
import * as models from '../../app/api/models'
import * as dates from '../../app/utils/date-value'
import { createHash } from 'node:crypto'
import { ProjectionResponseOption, RequestPolicyOption, ResponseMetadata } from '../../app/lib/api'

type TestRequest = { path: Record<string, string>; query: Record<string, unknown>; body: unknown; headers: Headers; signal?: AbortSignal }
type Handler = (request: TestRequest) => unknown | Promise<unknown>

export function testId(label: string): string {
  const value = createHash('sha256').update(label).digest('hex')
  return `${value.slice(0, 8)}-${value.slice(8, 12)}-4${value.slice(13, 16)}-8${value.slice(17, 20)}-${value.slice(20, 32)}`
}

/** Controller tests execute real generated builders against a deterministic in-process HTTP boundary. */
export function createTestApi(routes: Record<string, Handler>) {
  const transport: Middleware = {
    next: undefined,
    async execute(url: string, init: RequestInit, options?: Record<string, RequestOption>) {
      const target = new URL(url)
      for (const [route, handler] of Object.entries(routes)) {
        const [method, template] = route.split(' ')
        if (method !== init.method) continue
        const parameters: string[] = []
        const regex = template!.replace(/\{(\w+)\}/g, (_, name) => { parameters.push(name); return '([^/]+)' })
        const match = new RegExp(`^${regex}$`).exec(target.pathname)
        if (!match) continue
        const policy = (options?.NoCtfRequestPolicy as RequestPolicyOption | undefined)?.policy
        const query: Record<string, unknown> = {}
        for (const [key, value] of target.searchParams) {
          const parsed = ['offset', 'limit', 'endingRound'].includes(key) ? Number(value)
            : value === 'true' ? true : value === 'false' ? false : value
          query[key] = key in query ? [query[key], parsed].flat() : parsed
        }
        const request = new Request(url, init)
        const body = init.body ? await request.json() : undefined
        const result = await handler({ path: Object.fromEntries(parameters.map((name, index) => [name, decodeURIComponent(match[index + 1]!)])),
          query, body, headers: request.headers, signal: policy?.signal })
        const response = result instanceof Response ? result : result === undefined
          ? new Response(null, { status: 204 }) : Response.json(result)
        if (policy?.response) { policy.response.status = response.status; policy.response.headers = response.headers }
        return response
      }
      throw new Error(`Unexpected test API request: ${init.method} ${target.pathname}`)
    },
  }
  const adapter = new FetchRequestAdapter(new AnonymousAuthenticationProvider(), undefined, undefined, new HttpClient(undefined, transport))
  adapter.baseUrl = 'http://localhost'
  return createNoCtfClient(adapter)
}

export function kiotaBindings(source: string) {
  const factories = Object.fromEntries(Object.entries(models).filter(([name]) => source.includes(name)))
  return { ...factories, ...dates, ProjectionResponseOption, RequestPolicyOption, ResponseMetadata }
}
