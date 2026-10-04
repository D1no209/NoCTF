import {
  AnonymousAuthenticationProvider, MultipartBody, NativeResponseHandler, ResponseHandlerOption,
  type AuthenticationProvider, type RequestOption, type Parsable, type ParsableFactory, type RequestInformation,
} from '@microsoft/kiota-abstractions'
import { FetchRequestAdapter, HttpClient, type Middleware } from '@microsoft/kiota-http-fetchlibrary'
import { JsonParseNodeFactory } from '@microsoft/kiota-serialization-json'
import { createNoCtfClient } from '../api/noCtfClient'
import { currentLocale } from '../utils/i18n'
import { prepareCommandRequest, observeCommandResponse } from '../utils/command-attempt'
import { accessTokenSubject } from './auth-refresh'

export class ResponseMetadata {
  status = 0
  headers = new Headers()
  get ok() { return this.status >= 200 && this.status < 300 }
}

export class RequestPolicyOption implements RequestOption {
  constructor(readonly policy: { signal?: AbortSignal; cache?: RequestCache; response?: ResponseMetadata } = {}) {}
  getKey() { return 'NoCtfRequestPolicy' }
}

/** A projection's 202 body is not its completed 200 model. Each request owns its handler. */
export class ProjectionResponseOption<T extends Parsable> extends ResponseHandlerOption {
  constructor(factory: ParsableFactory<T>) {
    super()
    this.responseHandler = {
      async handleResponse<Native, Model>(native: Native): Promise<Model | undefined> {
        if (!(native instanceof Response)) throw new Error('Expected an HTTP response.')
        if (!native.ok) {
          let problem: Record<string, unknown> = {}
          try { problem = await native.json() } catch { /* Preserve status for empty responses. */ }
          throw { ...problem, responseStatusCode: native.status }
        }
        if (native.status === 202 || native.status === 204) return undefined
        const node = new JsonParseNodeFactory().getRootParseNode('application/json', await native.arrayBuffer())
        return node.getObjectValue(factory) as unknown as Model | undefined
      },
    }
  }
}

export interface SessionHooks {
  token(): string | null
  refresh(): Promise<boolean>
  impersonating(): boolean
  endImpersonation(): void
}
let session: SessionHooks | undefined

class FailureResponseOption implements RequestOption {
  status?: number
  authenticatedRequest = false
  problem?: Record<string, unknown>
  getKey() { return 'NoCtfFailureResponse' }
}

class BrowserRequestAdapter extends FetchRequestAdapter {
  constructor(authentication: AuthenticationProvider, transport: HttpClient) {
    super(authentication, undefined, undefined, transport)
    const send = this.send
    const sendCollection = this.sendCollection
    const sendPrimitive = this.sendPrimitive
    const sendPrimitives = this.sendCollectionOfPrimitive
    const sendEmpty = this.sendNoResponseContent
    const sendEnum = this.sendEnum
    const sendEnums = this.sendCollectionOfEnum
    this.send = (...args) => this.withFailureDetails(args[0], () => send(...args))
    this.sendCollection = (...args) => this.withFailureDetails(args[0], () => sendCollection(...args))
    this.sendPrimitive = (...args) => this.withFailureDetails(args[0], () => sendPrimitive(...args))
    this.sendCollectionOfPrimitive = (...args) => this.withFailureDetails(args[0], () => sendPrimitives(...args))
    this.sendNoResponseContent = (...args) => this.withFailureDetails(args[0], () => sendEmpty(...args))
    this.sendEnum = (...args) => this.withFailureDetails(args[0], () => sendEnum(...args))
    this.sendCollectionOfEnum = (...args) => this.withFailureDetails(args[0], () => sendEnums(...args))
  }

  private async withFailureDetails<T>(request: RequestInformation, send: () => Promise<T>): Promise<T> {
    const failure = new FailureResponseOption()
    request.addRequestOptions([failure])
    try { return await send() }
    catch (error) {
      if (failure.status === undefined) throw error
      // Preserve generated error models while retaining documented extension fields and unmapped HTTP failures.
      throw Object.assign(error && typeof error === 'object' ? error : {}, failure.problem,
        { responseStatusCode: failure.status, status: failure.status, authenticatedRequest: failure.authenticatedRequest })
    }
  }
}

class BrowserTransport implements Middleware {
  next: Middleware | undefined
  constructor(private readonly authenticated: boolean) {}

  async execute(url: string, init: RequestInit, options?: Record<string, RequestOption>): Promise<Response> {
    const policy = (options?.NoCtfRequestPolicy as RequestPolicyOption | undefined)?.policy
    const request = new Request(url, { ...init, credentials: 'same-origin', signal: policy?.signal, cache: policy?.cache })
    request.headers.set('Accept-Language', currentLocale())
    const body = ['GET', 'HEAD', 'OPTIONS'].includes(request.method) ? undefined
      : request.headers.get('content-type')?.startsWith('multipart/form-data')
        ? await request.clone().formData() : await request.clone().text()
    await prepareCommandRequest(request, body)
    const retry = request.clone()
    let response: Response
    try {
      response = await fetch(request, { credentials: 'same-origin' })
      const sentToken = request.headers.get('Authorization')
      if (this.authenticated && response.status === 401 && sentToken && session) {
        const originalToken = sentToken.replace(/^Bearer /i, '')
        const currentToken = session.token()
        const sameIdentity = (token: string | null) => Boolean(token && (token === originalToken
          || accessTokenSubject(originalToken) && accessTokenSubject(originalToken) === accessTokenSubject(token)))
        if (currentToken === originalToken && session.impersonating()) session.endImpersonation()
        else if (!session.impersonating() && sameIdentity(currentToken)) {
          const refreshed = currentToken !== originalToken || await session.refresh()
          const refreshedToken = session.token()
          if (refreshed && sameIdentity(refreshedToken) && !policy?.signal?.aborted) {
            retry.headers.set('Authorization', `Bearer ${refreshedToken}`)
            retry.headers.set('Accept-Language', currentLocale())
            response = await fetch(retry, { credentials: 'same-origin' })
          }
        }
      }
    }
    catch (error) { observeCommandResponse(request, undefined, true); throw error }
    if (policy?.response) { policy.response.status = response.status; policy.response.headers = response.headers }
    observeCommandResponse(request, response.status, !response.ok)
    if (!response.ok) {
      const content = await response.clone().text()
      const failure = options?.NoCtfFailureResponse as FailureResponseOption | undefined
      if (failure) {
        failure.status = response.status
        failure.authenticatedRequest = request.headers.has('Authorization')
        try {
          const problem: unknown = JSON.parse(content)
          if (problem && typeof problem === 'object' && !Array.isArray(problem))
            failure.problem = problem as Record<string, unknown>
        } catch { /* Bare HTTP errors and non-JSON errors keep their actual status. */ }
      }
      if (!content.trim())
        throw { responseStatusCode: response.status, responseHeaders: Object.fromEntries(response.headers) }
    }
    return response
  }
}

export function createApiClient(authenticated = true) {
  const authentication: AuthenticationProvider = authenticated ? {
    async authenticateRequest(request) {
      const token = session?.token()
      if (token) {
        request.headers.delete('Authorization')
        request.headers.add('Authorization', `Bearer ${token}`)
      }
    },
  } : new AnonymousAuthenticationProvider()
  // An explicit terminal transport excludes Kiota's automatic retry middleware.
  const adapter = new BrowserRequestAdapter(authentication, new HttpClient(undefined, new BrowserTransport(authenticated)))
  adapter.baseUrl = typeof location === 'undefined' ? 'http://localhost' : location.origin
  return createNoCtfClient(adapter)
}

export let api = createApiClient()
export function initializeApiClient(hooks: SessionHooks) { session = hooks; api = createApiClient() }

/** Serialize browser files through Kiota while preserving field names and filenames. */
export async function multipartBody(fields: Record<string, unknown>): Promise<MultipartBody> {
  const result = new MultipartBody()
  for (const [name, value] of Object.entries(fields)) {
    if (value === undefined || value === null) continue
    const values = Array.isArray(value) ? value : [value]
    for (let index = 0; index < values.length; index++) {
      const item = values[index]
      const field = Array.isArray(value) ? `${name}[${index}]` : name
      if (item instanceof Blob)
        result.addOrReplacePart(field, item.type || 'application/octet-stream', await item.arrayBuffer(), undefined,
          item instanceof File ? item.name : 'upload')
      else result.addOrReplacePart(field, 'text/plain', String(item))
      // Kiota keys parts by name; ASP.NET binds file collections from repeated original field names.
      if (Array.isArray(value)) result.listParts()[field.toLocaleLowerCase()]!.originalName = name
    }
  }
  return result
}

export async function nativeResponse(invoke: (options: RequestOption[]) => Promise<unknown>): Promise<Response> {
  const handler = new NativeResponseHandler()
  const option = new ResponseHandlerOption()
  option.responseHandler = handler
  await invoke([option])
  if (!(handler.value instanceof Response)) throw new Error('The transport did not return a Response.')
  const response = handler.value
  if (!response.ok) {
    const text = await response.text()
    let problem: Record<string, unknown> = {}
    try { problem = JSON.parse(text) } catch { /* Preserve HTTP status for an empty error. */ }
    throw { ...problem, responseStatusCode: response.status }
  }
  return response
}

export function binaryResponse(invoke: (options: RequestOption[]) => Promise<unknown>, mode: 'blob'): Promise<Blob>
export function binaryResponse(invoke: (options: RequestOption[]) => Promise<unknown>, mode: 'stream'): Promise<ReadableStream<Uint8Array>>
export function binaryResponse(invoke: (options: RequestOption[]) => Promise<unknown>, mode: 'blob' | 'stream'): Promise<Blob | ReadableStream<Uint8Array>>
export async function binaryResponse(invoke: (options: RequestOption[]) => Promise<unknown>, mode: 'blob' | 'stream'): Promise<Blob | ReadableStream<Uint8Array>> {
  const response = await nativeResponse(invoke)
  if (mode === 'blob') return response.blob()
  if (!response.body) throw new Error('The download response has no body.')
  return response.body
}
