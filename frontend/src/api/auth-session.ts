export interface AuthSession {
  accessToken: string
  userName: string
  role: string
}

const AUTH_REFRESH_WINDOW_MS = 2 * 60 * 1000
const DEFAULT_REFRESH_TIMEOUT_MS = 12_000
const TRAILING_SLASH_RE = /\/$/

type SessionListener = (session: AuthSession | null) => void

const listeners = new Set<SessionListener>()
let refreshFetch: typeof fetch = globalThis.fetch
let refreshBaseUrl = ''
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
  const accessToken = localStorage.getItem('accessToken')
  const storedUser = localStorage.getItem('authUser')
  if (!accessToken || !storedUser)
    return null

  try {
    const user = JSON.parse(storedUser) as Partial<Pick<AuthSession, 'userName' | 'role'>>
    if (typeof user.userName === 'string' && typeof user.role === 'string')
      return { accessToken, userName: user.userName, role: user.role }
  }
  catch {
    // Invalid persisted data is cleared below.
  }

  clearAuthSession()
  return null
}

export function saveAuthSession(session: AuthSession) {
  localStorage.setItem('accessToken', session.accessToken)
  localStorage.setItem('authUser', JSON.stringify({ userName: session.userName, role: session.role }))
  notifySessionChanged(session)
}

export function clearAuthSession() {
  localStorage.removeItem('accessToken')
  localStorage.removeItem('authUser')
  notifySessionChanged(null)
}

export function subscribeAuthSession(listener: SessionListener) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function configureAuthSessionRefresh(
  baseFetch: typeof fetch,
  baseUrl: string,
  timeoutMs = DEFAULT_REFRESH_TIMEOUT_MS,
) {
  refreshFetch = baseFetch
  refreshBaseUrl = baseUrl.replace(TRAILING_SLASH_RE, '')
  refreshTimeoutMs = timeoutMs
}

export async function refreshAuthSessionIfNeeded(force = false): Promise<AuthSession | null> {
  const session = readAuthSession()
  if (!session || (!force && !shouldRefreshToken(session.accessToken)))
    return session

  if (refreshInFlight)
    return refreshInFlight

  refreshInFlight = refreshAuthSession(session)
    .finally(() => {
      refreshInFlight = null
    })

  return refreshInFlight
}

async function refreshAuthSession(session: AuthSession): Promise<AuthSession | null> {
  const controller = new AbortController()
  const timeout = globalThis.setTimeout(() => controller.abort(), refreshTimeoutMs)

  try {
    const response = await refreshFetch(`${refreshBaseUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        Authorization: `Bearer ${session.accessToken}`,
      },
      signal: controller.signal,
    })

    if (response.status === 401 || response.status === 403) {
      if (localStorage.getItem('accessToken') === session.accessToken)
        clearAuthSession()
      return null
    }

    if (!response.ok)
      throw new Error(`Session refresh failed with HTTP ${response.status}.`)

    const data = await response.json() as Partial<AuthSession>
    if (
      typeof data.accessToken !== 'string'
      || typeof data.userName !== 'string'
      || typeof data.role !== 'string'
    ) {
      throw new TypeError('Session refresh returned an invalid response.')
    }

    const refreshed = {
      accessToken: data.accessToken,
      userName: data.userName,
      role: data.role,
    }

    if (localStorage.getItem('accessToken') === session.accessToken)
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
