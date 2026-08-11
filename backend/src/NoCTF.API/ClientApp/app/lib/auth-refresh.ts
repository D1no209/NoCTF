const anonymousAuthenticationPaths = new Set([
  '/api/v1/auth/login',
  '/api/v1/auth/logout',
  '/api/v1/auth/refresh',
  '/api/v1/auth/register',
  '/api/v1/auth/password-reset/request',
  '/api/v1/auth/password-reset/complete',
  '/api/v1/auth/email-verification/verify',
])

function requestPath(requestUrl: string): string {
  const path = new URL(requestUrl, 'http://localhost').pathname
  return path.length > 1 ? path.replace(/\/+$/, '') : path
}

export function shouldRefreshSession(responseStatus: number, requestUrl: string): boolean {
  return responseStatus === 401 && !anonymousAuthenticationPaths.has(requestPath(requestUrl))
}
