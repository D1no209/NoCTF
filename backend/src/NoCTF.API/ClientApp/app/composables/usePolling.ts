import { onScopeDispose, ref } from 'vue'

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

  let disposed = false
  /**
   * Run token: start()/stop() bump it so any in-flight tick chain becomes stale.
   * Without it a probe that is awaiting while start() runs resumes into a second
   * chain, and two chains keep scheduling the same `timer` handle concurrently.
   */
  let run = 0
  let timer: ReturnType<typeof setTimeout> | undefined
  let deadlineTimer: ReturnType<typeof setTimeout> | undefined

  async function tick(token: number, delay: number, elapsed: number, attempt: number): Promise<void> {
    if (disposed || token !== run) return
    if (elapsed >= timeout) {
      polling.value = false
      timedOut.value = true
      return
    }
    timer = setTimeout(async () => {
      if (disposed || token !== run) return
      let done = false
      try {
        done = await probe()
        error.value = null
      }
      catch (probeError) {
        error.value = probeError
        // Keep polling through transient failures until the timeout.
      }
      if (done || disposed || token !== run) {
        if (token === run) {
          polling.value = false
          if (deadlineTimer) clearTimeout(deadlineTimer)
        }
        return
      }
      const nextDelay = delays[attempt + 1]
        ?? Math.min(delay * 1.5, maxInterval)
      await tick(token, nextDelay, elapsed + delay, attempt + 1)
    }, delay)
  }

  function start(): void {
    // A disposed scope must never arm a new chain: late await continuations would
    // otherwise keep polling with no remaining cleanup path.
    if (disposed) return
    stop()
    const token = ++run
    timedOut.value = false
    error.value = null
    polling.value = true
    deadlineTimer = setTimeout(() => {
      if (disposed || token !== run) return
      run += 1
      if (timer) clearTimeout(timer)
      polling.value = false
      timedOut.value = true
    }, timeout)
    void tick(token, delays[0] ?? interval, 0, 0)
  }

  function stop(): void {
    run += 1
    polling.value = false
    timedOut.value = false
    error.value = null
    if (timer) clearTimeout(timer)
    if (deadlineTimer) clearTimeout(deadlineTimer)
    timer = undefined
    deadlineTimer = undefined
  }

  onScopeDispose(() => { disposed = true; stop() })

  return { polling, timedOut, error, start, stop }
}
