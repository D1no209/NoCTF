export type ChallengeTestRuntimeLoadOutcome = 'available' | 'missing' | 'failed'
export type ChallengeTestRuntimeMutationKind = 'start' | 'stop' | 'reset' | 'extend'

export interface ChallengeTestRuntimePollingSnapshot {
  id?: string | null
  state?: 'Queued' | 'Provisioning' | 'Running' | 'Stopping' | 'Stopped' | 'Failed' | null
  flagState?: 'NotRequired' | 'Pending' | 'Succeeded' | 'Failed' | 'Canceled' | null
  expiresAt?: Date | string | null
}

export interface PendingChallengeTestRuntimeMutation {
  kind: ChallengeTestRuntimeMutationKind
  runtimeInstanceId?: string | null
  previousRuntimeInstanceId?: string | null
  previousExpiresAt?: Date | string | null
}

export interface ChallengeTestRuntimePollingDecision {
  continuePolling: boolean
  mutationObserved: boolean
}

function needsLifecyclePolling(runtime: ChallengeTestRuntimePollingSnapshot | null): boolean {
  return runtime?.state === 'Queued'
    || runtime?.state === 'Provisioning'
    || runtime?.state === 'Stopping'
    || runtime?.state === 'Running' && runtime.flagState === 'Pending'
}

function hasObservedMutation(
  runtime: ChallengeTestRuntimePollingSnapshot,
  pending: PendingChallengeTestRuntimeMutation,
): boolean {
  if (pending.runtimeInstanceId && runtime.id !== pending.runtimeInstanceId)
    return false

  switch (pending.kind) {
    case 'start':
      return Boolean(runtime.id)
    case 'reset':
      return Boolean(runtime.id)
        && runtime.id !== pending.previousRuntimeInstanceId
    case 'stop':
      return runtime.state === 'Stopping'
        || runtime.state === 'Stopped'
        || runtime.state === 'Failed'
    case 'extend':
      return Boolean(runtime.expiresAt)
        && runtime.expiresAt !== pending.previousExpiresAt
  }
}

export function evaluateChallengeTestRuntimePolling(
  outcome: ChallengeTestRuntimeLoadOutcome,
  runtime: ChallengeTestRuntimePollingSnapshot | null,
  pending: PendingChallengeTestRuntimeMutation | null,
): ChallengeTestRuntimePollingDecision {
  if (outcome === 'failed')
    return { continuePolling: true, mutationObserved: false }
  if (outcome === 'missing') {
    return {
      continuePolling: pending !== null,
      mutationObserved: false,
    }
  }

  const mutationObserved = pending !== null && runtime !== null
    ? hasObservedMutation(runtime, pending)
    : false
  return {
    continuePolling: pending !== null && !mutationObserved
      || needsLifecyclePolling(runtime),
    mutationObserved,
  }
}
