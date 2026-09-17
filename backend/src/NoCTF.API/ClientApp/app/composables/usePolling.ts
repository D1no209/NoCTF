export interface PollingOptions {
  /** Initial delay in ms. */
  interval?: number
  /** Maximum delay in ms (exponential backoff cap). */
  maxInterval?: number
  /** Overall timeout in ms; polling stops with `timedOut` set afterwards. */
  timeout?: number
  /** Optional operation-specific delays before ordinary exponential backoff resumes. */
  delays?: readonly number[]
}

/**
 * Poll an async probe until it reports completion, the timeout hits, or `stop()` is called.
 * Used for 202 + statusUrl flows (submissions, runtime operations, leaderboard projection).
 */
export function usePolling(probe: () => Promise<boolean>, options: PollingOptions = {}) {
  const { interval = 1000, maxInterval = 8000, timeout = 30_000, delays = [] } = options

  const polling = ref(false)
  const timedOut = ref(false)
  const error = ref<unknown | null>(null)

  let stopped = false
  let timer: ReturnType<typeof setTimeout> | undefined

  async function tick(delay: number, elapsed: number, attempt: number): Promise<void> {
    if (stopped) return
    if (elapsed >= timeout) {
      polling.value = false
      timedOut.value = true
      return
    }
    timer = setTimeout(async () => {
      if (stopped) return
      let done = false
      try {
        done = await probe()
        error.value = null
      }
      catch (probeError) {
        error.value = probeError
        // Keep polling through transient failures until the timeout.
      }
      if (done || stopped) {
        polling.value = false
        return
      }
      const nextDelay = delays[attempt + 1]
        ?? Math.min(delay * 1.5, maxInterval)
      await tick(nextDelay, elapsed + delay, attempt + 1)
    }, delay)
  }

  function start(): void {
    stop()
    stopped = false
    timedOut.value = false
    error.value = null
    polling.value = true
    void tick(delays[0] ?? interval, 0, 0)
  }

  function stop(): void {
    stopped = true
    polling.value = false
    timedOut.value = false
    error.value = null
    if (timer) clearTimeout(timer)
  }

  onScopeDispose(stop)

  return { polling, timedOut, error, start, stop }
}
