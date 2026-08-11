import type { NoCtfapiEndpointsRuntimeRuntimeResponse } from '../api'

type PlayerRuntime = NoCtfapiEndpointsRuntimeRuntimeResponse
type RuntimeStateValue = NonNullable<PlayerRuntime['state']>

export type PlayerRuntimeLookupOutcome = 'available' | 'missing' | 'failed'

const TRANSITIONAL_STATES = new Set<RuntimeStateValue>([
  'Queued',
  'Provisioning',
  'Stopping',
])

export function normalizePlayerRuntime(runtime: PlayerRuntime | null): PlayerRuntime | null {
  return runtime?.state === 'Stopped' ? null : runtime
}

export function classifyPlayerRuntimeLookup(
  status: number | undefined,
  hasError: boolean,
  hasData: boolean,
): PlayerRuntimeLookupOutcome {
  if (status === 404) return 'missing'
  if (hasError || !hasData) return 'failed'
  return 'available'
}

export function shouldPollPlayerRuntime(runtime: PlayerRuntime | null, now: number): boolean {
  if (!runtime?.state) return false
  if (TRANSITIONAL_STATES.has(runtime.state)) return true
  if (runtime.state !== 'Running' || !runtime.expiresAt) return false

  const expiresAt = Date.parse(runtime.expiresAt)
  return Number.isFinite(expiresAt) && expiresAt <= now
}
