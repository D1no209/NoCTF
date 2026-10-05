export interface RuntimeExtensionRequest {
  minutes: number
  expiresAt: string
}

export const RUNTIME_RENEWAL_WINDOW_MS = 10 * 60_000

export function isRuntimeExtensionWindowOpen(currentExpiry: string | null | undefined, now: number): boolean {
  const expiry = currentExpiry ? Date.parse(currentExpiry) : Number.NaN
  return Number.isFinite(expiry) && expiry > now && expiry - now <= RUNTIME_RENEWAL_WINDOW_MS
}

export function isRuntimeExtensionTooEarly(currentExpiry: string | null | undefined, now: number): boolean {
  const expiry = currentExpiry ? Date.parse(currentExpiry) : Number.NaN
  return Number.isFinite(expiry) && expiry - now > RUNTIME_RENEWAL_WINDOW_MS
}

export function parseRuntimeExtensionMinutes(value: unknown, maximum: number): number | null {
  if (value === '' || value === null || value === undefined) return null
  const minutes = typeof value === 'number' || typeof value === 'string'
    ? Number(value)
    : Number.NaN
  return Number.isInteger(minutes) && minutes >= 1 && minutes <= maximum
    ? minutes
    : null
}

export function createRuntimeExtensionRequest(
  currentExpiry: string | null | undefined,
  now: number,
  value: unknown,
  maximum: number,
): RuntimeExtensionRequest | null {
  const minutes = parseRuntimeExtensionMinutes(value, maximum)
  if (minutes === null || !isRuntimeExtensionWindowOpen(currentExpiry, now)) return null
  const expiry = Date.parse(currentExpiry ?? '')
  try {
    return { minutes, expiresAt: new Date(expiry + minutes * 60_000).toISOString() }
  }
  catch {
    return null
  }
}
