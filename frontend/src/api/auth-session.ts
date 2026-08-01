import type {
  NoCtfapiEndpointsAuthenticationRefreshTokenResponse,
  NoCtfDomainIdentityUserRole,
} from './generated/types.gen'
import { isPlatformUserRole } from './userRole'

export interface AuthSession {
  accessToken: string
  userName: string
  role: NoCtfDomainIdentityUserRole
}

const AUTH_REFRESH_WINDOW_MS = 2 * 60 * 1000
const DEFAULT_REFRESH_TIMEOUT_MS = 12_000

type SessionListener = (session: AuthSession | null) => void

interface AuthSessionRefreshResult {
  data?: NoCtfapiEndpointsAuthenticationRefreshTokenResponse
  error?: unknown
  response?: Response
}

type AuthSessionRefreshOperation = (signal: AbortSignal) => Promise<AuthSessionRefreshResult>

const listeners = new Set<SessionListener>()
// Access tokens are deliberately process-memory only. The refresh token remains
// in the server-managed HttpOnly cookie and is never readable by JavaScript.
let memorySession: AuthSession | null = null
let refreshOperation: AuthSessionRefreshOperation | null = null
let refreshTimeoutMs = DEFAULT_REFRESH_TIMEOUT_MS
let refreshInFlight: Promise<AuthSession | null> | null = null

export function getTokenExpiry(token: string | null): number | null {
  if (!token)
    return null

  try {
    const payload = token.split('.')[1]
    if (!payload)
      return null

    const normalizedPayload = payload.replace(/-/g, '+').replace(/_/g, '/')
    const paddedPayload = normalizedPayload.padEnd(Math.ceil(normalizedPayload.length / 4) * 4, '=')
    const decoded = JSON.parse(atob(paddedPayload)) as { exp?: unknown }
    return typeof decoded.exp === 'number' ? decoded.exp * 1000 : null
  }
  catch {
    return null
  }
}

export function isTokenExpired(token: string | null, now = Date.now()) {
  const expiresAt = getTokenExpiry(token)
  return expiresAt !== null && expiresAt <= now
}

export function shouldRefreshToken(token: string | null, now = Date.now()) {
  const expiresAt = getTokenExpiry(token)
  return expiresAt !== null && expiresAt <= now + AUTH_REFRESH_WINDOW_MS
}

export function millisecondsUntilTokenRefresh(token: string | null, now = Date.now()) {
  const expiresAt = getTokenExpiry(token)
  return expiresAt === null ? null : Math.max(0, expiresAt - now - AUTH_REFRESH_WINDOW_MS)
}

export function readAuthSession(): AuthSession | null {
  return memorySession
}

export function saveAuthSession(session: AuthSession) {
  memorySession = session
  notifySessionChanged(session)
}

export function clearAuthSession() {
  memorySession = null
  notifySessionChanged(null)
}

export function subscribeAuthSession(listener: SessionListener) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function configureAuthSessionRefresh(
  operation: AuthSessionRefreshOperation,
  timeoutMs = DEFAULT_REFRESH_TIMEOUT_MS,
) {
  refreshOperation = operation
  refreshTimeoutMs = timeoutMs
}

export async function refreshAuthSessionIfNeeded(force = false): Promise<AuthSession | null> {
  const session = readAuthSession()
  if (!session && !force)
    return null
  if (session && !force && !shouldRefreshToken(session.accessToken))
    return session

  if (refreshInFlight)
    return refreshInFlight

  refreshInFlight = refreshAuthSession(session)
    .finally(() => {
      refreshInFlight = null
    })

  return refreshInFlight
}

async function refreshAuthSession(session: AuthSession | null): Promise<AuthSession | null> {
  const controller = new AbortController()
  const timeout = globalThis.setTimeout(() => controller.abort(), refreshTimeoutMs)

  try {
    if (!refreshOperation)
      throw new Error('Session refresh operation is not configured.')

    const result = await refreshOperation(controller.signal)
    const status = result.response?.status
    if (status === 401 || status === 403) {
      if (memorySession?.accessToken === session?.accessToken)
        clearAuthSession()
      return null
    }

    if (result.error !== undefined || (result.response && !result.response.ok)) {
      const statusDescription = status === undefined ? '' : ` with HTTP ${status}`
      throw new Error(`Session refresh failed${statusDescription}.`)
    }

    const data = result.data
    if (
      !data
      || typeof data.accessToken !== 'string'
      || typeof data.userName !== 'string'
      || !isPlatformUserRole(data.role)
    ) {
      throw new TypeError('Session refresh returned an invalid response.')
    }

    const refreshed = {
      accessToken: data.accessToken,
      userName: data.userName,
      role: data.role,
    }

    if (memorySession?.accessToken === session?.accessToken)
      saveAuthSession(refreshed)

    return readAuthSession()
  }
  finally {
    globalThis.clearTimeout(timeout)
  }
}

function notifySessionChanged(session: AuthSession | null) {
  for (const listener of listeners)
    listener(session)
}
