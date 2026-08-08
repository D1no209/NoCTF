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
 * Uses a raw fetch instead of the generated SDK to avoid interceptor recursion.
 * Concurrent callers share a single in-flight request.
 */
export function refreshSession(): Promise<boolean> {
  refreshPromise ??= (async () => {
    try {
      const response = await fetch('/api/v1/auth/refresh', {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json' },
      })
      if (!response.ok) {
        setAccessToken(null)
        return false
      }
      const data = (await response.json()) as { accessToken?: string }
      if (!data.accessToken) {
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
