const ACCESS_TOKEN_REFRESH_SKEW_SECONDS = 30

function jwtExpiry(token: string): number | null {
  const payload = token.split('.')[1]
  if (!payload) return null
  try {
    const base64 = payload.replaceAll('-', '+').replaceAll('_', '/')
      .padEnd(Math.ceil(payload.length / 4) * 4, '=')
    const parsed = JSON.parse(atob(base64)) as { exp?: unknown }
    return typeof parsed.exp === 'number' && Number.isFinite(parsed.exp) ? parsed.exp : null
  }
  catch {
    return null
  }
}

export function requiresInteractiveAuthentication(code: unknown): boolean {
  return typeof code === 'string' && ['PrimaryAuthenticationRequired', 'LocalMfaRequired', 'NotEnrolled', 'PolicyChanged', 'AccountUnavailable'].includes(code)
}
export function shouldRefreshSession(responseStatus: number, requestHeaders: Headers, path = '', code?: unknown): boolean {
  return responseStatus === 401 && requestHeaders.has('Authorization') && !path.includes('/auth/mfa/') && !path.includes('/auth/passkeys/') && !requiresInteractiveAuthentication(code)
}

export function accessTokenNeedsRefresh(
  token: string,
  nowSeconds = Date.now() / 1000,
): boolean {
  const expiresAt = jwtExpiry(token)
  return expiresAt === null || expiresAt <= nowSeconds + ACCESS_TOKEN_REFRESH_SKEW_SECONDS
}
