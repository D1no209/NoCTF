export const REALTIME_RETRY_DELAYS_MS = [1_000, 2_000, 5_000, 10_000, 30_000] as const

export type RealtimeRetryWait = (delayMs: number, signal: AbortSignal) => Promise<void>

export function waitForRealtimeRetry(delayMs: number, signal: AbortSignal): Promise<void> {
  if (signal.aborted) return Promise.resolve()

  return new Promise((resolve) => {
    const finish = (): void => {
      clearTimeout(timer)
      signal.removeEventListener('abort', finish)
      resolve()
    }
    const timer = setTimeout(finish, delayMs)
    signal.addEventListener('abort', finish, { once: true })
  })
}

/**
 * Retry an initial realtime connection with one in-flight attempt at a time.
 * The delay grows to a 30-second ceiling and stops immediately when cancelled
 * or when the owning subscriber is no longer active.
 */
export async function startRealtimeWithRetry(
  start: () => Promise<void>,
  signal: AbortSignal,
  canAttempt: () => boolean,
  wait: RealtimeRetryWait = waitForRealtimeRetry,
): Promise<boolean> {
  let failureCount = 0

  while (!signal.aborted && canAttempt()) {
    if (failureCount > 0) {
      const delayIndex = Math.min(failureCount - 1, REALTIME_RETRY_DELAYS_MS.length - 1)
      await wait(REALTIME_RETRY_DELAYS_MS[delayIndex]!, signal)
      if (signal.aborted || !canAttempt()) return false
    }

    try {
      await start()
      return !signal.aborted && canAttempt()
    }
    catch {
      failureCount++
    }
  }

  return false
}
