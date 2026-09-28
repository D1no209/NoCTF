import { reactive } from 'vue'

export type RuntimeOperationKind = 'start' | 'reset' | 'terminate' | 'force-terminate' | 'extend'
export type RuntimeRefreshResult = 'settled' | 'exhausted' | 'cancelled'

export interface RuntimeOperationToken {
  readonly key: string
  readonly id: number
}

interface RuntimeOperationEntry {
  controller: AbortController
  token: RuntimeOperationToken
}

export interface RuntimeOperationCoordinatorOptions {
  maxAttempts?: number
  intervalMs?: number
  maxIntervalMs?: number
  delaysMs?: readonly number[]
  timeoutMs?: number
  wait?: (delayMs: number, signal: AbortSignal) => Promise<void>
}

function waitForDelay(delayMs: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    if (signal.aborted) {
      resolve()
      return
    }

    const finish = () => {
      clearTimeout(timer)
      signal.removeEventListener('abort', finish)
      resolve()
    }
    const timer = setTimeout(finish, delayMs)
    signal.addEventListener('abort', finish, { once: true })
  })
}

export function createRuntimeOperationCoordinator(options: RuntimeOperationCoordinatorOptions = {}) {
  const {
    maxAttempts = 12,
    intervalMs = 2_000,
    maxIntervalMs = 8_000,
    delaysMs = [],
    timeoutMs = 30_000,
    wait = waitForDelay,
  } = options

  const pending = reactive(new Map<string, RuntimeOperationKind>())
  const active = new Map<string, RuntimeOperationEntry>()
  let nextTokenId = 0
  let disposed = false

  function begin(key: string, operation: RuntimeOperationKind): RuntimeOperationToken | null {
    if (disposed || active.has(key)) return null

    const token = { key, id: ++nextTokenId }
    active.set(key, { controller: new AbortController(), token })
    pending.set(key, operation)
    return token
  }

  function currentEntry(token: RuntimeOperationToken): RuntimeOperationEntry | null {
    const entry = active.get(token.key)
    return entry?.token.id === token.id ? entry : null
  }

  function isActive(token: RuntimeOperationToken): boolean {
    return currentEntry(token) !== null
  }

  async function poll(
    token: RuntimeOperationToken,
    probe: (signal: AbortSignal) => Promise<boolean>,
  ): Promise<RuntimeRefreshResult> {
    const entry = currentEntry(token)
    if (!entry) return 'cancelled'

    let timedOut = false
    const timeout = setTimeout(() => {
      timedOut = true
      entry.controller.abort()
    }, timeoutMs)
    let previousDelay = intervalMs

    try {
      for (let attempt = 0; attempt < maxAttempts; attempt += 1) {
        if (!currentEntry(token) || entry.controller.signal.aborted)
          return timedOut ? 'exhausted' : 'cancelled'

        try {
          if (await probe(entry.controller.signal)) return 'settled'
        }
        catch {
          if (entry.controller.signal.aborted)
            return timedOut ? 'exhausted' : 'cancelled'
          // Transient refresh failures do not change the accepted operation state.
        }

        if (attempt + 1 < maxAttempts) {
          const scheduledDelay = delaysMs[attempt]
          const delay = scheduledDelay ?? Math.min(previousDelay * 1.5, maxIntervalMs)
          previousDelay = delay
          await wait(delay, entry.controller.signal)
        }
      }

      return 'exhausted'
    }
    finally {
      clearTimeout(timeout)
    }
  }

  function finish(token: RuntimeOperationToken): void {
    const entry = currentEntry(token)
    if (!entry) return

    entry.controller.abort()
    active.delete(token.key)
    pending.delete(token.key)
  }

  function cancelAll(): void {
    disposed = true
    for (const entry of active.values()) entry.controller.abort()
    active.clear()
    pending.clear()
  }

  return {
    pending,
    begin,
    isActive,
    poll,
    finish,
    cancelAll,
  }
}
