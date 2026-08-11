import { refreshTokenEndpoint } from '~/api'
import { createClient } from '~/api/client'
import { accessTokenNeedsRefresh } from './auth-refresh'

/**
 * In-memory access token store and refresh single-flight.
 *
 * The access token is intentionally kept only in memory (never localStorage);
 * the refresh token lives in an HttpOnly cookie managed by the backend.
 * Module scope is safe because the app is a client-only SPA (ssr: false).
 */
let accessToken: string | null = null

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
}

let refreshPromise: Promise<boolean> | null = null

/**
 * Exchange the refresh cookie for a new access token.
 * Uses an isolated generated client to avoid interceptor recursion.
 * Concurrent callers share a single in-flight request.
 */
export function refreshSession(): Promise<boolean> {
  refreshPromise ??= (async () => {
    try {
      const refreshClient = createClient({ credentials: 'same-origin', fetch: globalThis.fetch })
      const { data, error } = await refreshTokenEndpoint({ client: refreshClient })
      if (error || !data?.accessToken) {
        setAccessToken(null)
        return false
      }
      setAccessToken(data.accessToken)
      return true
    }
    catch {
      setAccessToken(null)
      return false
    }
    finally {
      // Allow the next expiry to trigger a fresh refresh.
      setTimeout(() => {
        refreshPromise = null
      }, 0)
    }
  })()
  return refreshPromise
}

/** Return a non-expiring token for SignalR connect/reconnect requests. */
export async function getRealtimeAccessToken(): Promise<string> {
  const current = getAccessToken()
  if (current && !accessTokenNeedsRefresh(current)) return current
  return await refreshSession() ? getAccessToken() ?? '' : ''
}
