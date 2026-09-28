export interface RuntimeExtensionRequest {
  minutes: number
  expiresAt: string
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
  const expiry = currentExpiry ? Date.parse(currentExpiry) : Number.NaN
  if (minutes === null || !Number.isFinite(expiry) || expiry <= now) return null
  try {
    return { minutes, expiresAt: new Date(expiry + minutes * 60_000).toISOString() }
  }
  catch {
    return null
  }
}
