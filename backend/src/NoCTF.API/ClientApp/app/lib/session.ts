import { dateTimestamp } from '../utils/date-value'
import { createApiClient } from './api'
import { accessTokenNeedsRefresh } from './auth-refresh'

/**
 * In-memory access token store, reversible identity switch and refresh single-flight.
 *
 * Both the active and suspended administrator access tokens stay only in memory (never localStorage);
 * the refresh token lives in an HttpOnly cookie managed by the backend.
 * Module scope is safe because the app is a client-only SPA (ssr: false).
 */
let accessToken: string | null = null
let sessionRevision = 0
let sessionInvalidated: (() => void) | null = null
let impersonationActive = false
let administratorAccessToken: string | null = null
let impersonationTimer: ReturnType<typeof setTimeout> | null = null
let impersonationExpiresAt = 0
let impersonationEnded: ((reason: ImpersonationEndReason) => void | Promise<void>) | null = null

export type ImpersonationEndReason = 'expired' | 'unauthorized'

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  sessionRevision += 1
  accessToken = token
}

export function isImpersonatingSession(): boolean {
  return impersonationActive
}

export function beginImpersonationAccessToken(token: string, expiresAt: Date | string): boolean {
  if (impersonationActive || !accessToken) return false
  if (impersonationTimer) clearTimeout(impersonationTimer)
  administratorAccessToken = accessToken
  impersonationActive = true
  setAccessToken(token)
  impersonationExpiresAt = dateTimestamp(expiresAt)
  scheduleImpersonationExpiry()
  return true
}

function scheduleImpersonationExpiry(): void {
  const delay = Math.max(0, impersonationExpiresAt - Date.now())
  impersonationTimer = setTimeout(() => {
    if (Date.now() < impersonationExpiresAt) scheduleImpersonationExpiry()
    else void requestImpersonationEnd('expired')
  }, Math.min(delay, 2_147_483_647))
}

export function clearImpersonationAccessToken(): void {
  if (impersonationTimer) clearTimeout(impersonationTimer)
  impersonationTimer = null
  impersonationExpiresAt = 0
  impersonationActive = false
  administratorAccessToken = null
  setAccessToken(null)
}

export function restoreImpersonationAccessToken(): string | null {
  if (impersonationTimer) clearTimeout(impersonationTimer)
  const restored = administratorAccessToken
  impersonationTimer = null
  impersonationExpiresAt = 0
  impersonationActive = false
  administratorAccessToken = null
  setAccessToken(restored)
  return restored
}

export function setImpersonationEndHandler(
  handler: ((reason: ImpersonationEndReason) => void | Promise<void>) | null,
): () => void {
  impersonationEnded = handler
  return () => {
    if (impersonationEnded === handler) impersonationEnded = null
  }
}

export function requestImpersonationEnd(reason: ImpersonationEndReason): void {
  void impersonationEnded?.(reason)
}

export function setSessionInvalidationHandler(handler: (() => void) | null): () => void {
  sessionInvalidated = handler
  return () => {
    if (sessionInvalidated === handler) sessionInvalidated = null
  }
}

function invalidateSession(): void {
  setAccessToken(null)
  sessionInvalidated?.()
}

let refreshPromise: Promise<boolean> | null = null

/**
 * Exchange the refresh cookie for a new access token.
 * Uses an isolated generated client to avoid interceptor recursion.
 * Concurrent callers share a single in-flight request.
 */
export function refreshSession(): Promise<boolean> {
  if (impersonationActive) return Promise.resolve(false)
  return refreshSessionCore()
}

function refreshSessionCore(): Promise<boolean> {
  if (refreshPromise) return refreshPromise
  const expectedRevision = sessionRevision
  const attempt = (async () => {
    try {
      const refreshClient = createApiClient(false)
      const data = await refreshClient.api.v1.auth.refresh.post()
      if (sessionRevision !== expectedRevision || impersonationActive) return false
      if (!data?.accessToken) {
        invalidateSession()
        return false
      }
      setAccessToken(data.accessToken)
      return true
    }
    catch {
      if (sessionRevision === expectedRevision && !impersonationActive) invalidateSession()
      return false
    }
  })()
  refreshPromise = attempt
  void attempt.then(() => {
    if (refreshPromise === attempt) refreshPromise = null
  })
  return attempt
}

/** Return a non-expiring token for SignalR connect/reconnect requests. */
export async function getRealtimeAccessToken(): Promise<string> {
  const current = getAccessToken()
  if (current && impersonationActive) return current
  if (current && !accessTokenNeedsRefresh(current)) return current
  return await refreshSession() ? getAccessToken() ?? '' : ''
}
