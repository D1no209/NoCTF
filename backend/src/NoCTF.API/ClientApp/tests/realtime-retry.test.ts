import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import {
  REALTIME_RETRY_DELAYS_MS,
  startRealtimeWithRetry,
  waitForRealtimeRetry,
} from '../app/lib/realtime-retry'

describe('realtime initial connection retry', () => {
  test('retries serially and caps the backoff delay', async () => {
    const waits: number[] = []
    let attempts = 0
    let active = 0
    let maximumActive = 0

    const connected = await startRealtimeWithRetry(
      async () => {
        attempts++
        active++
        maximumActive = Math.max(maximumActive, active)
        await Promise.resolve()
        active--
        throw new Error('offline')
      },
      new AbortController().signal,
      () => attempts < 7,
      async delay => waits.push(delay),
    )

    expect(connected).toBeFalse()
    expect(maximumActive).toBe(1)
    expect(attempts).toBe(7)
    expect(waits).toEqual([
      ...REALTIME_RETRY_DELAYS_MS,
      REALTIME_RETRY_DELAYS_MS.at(-1),
    ])
  })

  test('connects after transient failures', async () => {
    let attempts = 0
    const connected = await startRealtimeWithRetry(
      async () => {
        attempts++
        if (attempts < 3) throw new Error('transient')
      },
      new AbortController().signal,
      () => true,
      async () => undefined,
    )

    expect(connected).toBeTrue()
    expect(attempts).toBe(3)
  })

  test('cancels during backoff without another connection attempt', async () => {
    const controller = new AbortController()
    let attempts = 0
    const connected = await startRealtimeWithRetry(
      async () => {
        attempts++
        throw new Error('offline')
      },
      controller.signal,
      () => true,
      async () => controller.abort(),
    )

    expect(connected).toBeFalse()
    expect(attempts).toBe(1)
  })

  test('cancels the real retry timer immediately', async () => {
    const controller = new AbortController()
    const wait = waitForRealtimeRetry(30_000, controller.signal)
    controller.abort()
    await wait
    expect(controller.signal.aborted).toBeTrue()
  })

  test('both hubs use the shared retry and cancel it during teardown', async () => {
    const competitionHub = await sourceFile(new URL('../app/composables/useCompetitionHub.ts', import.meta.url)).text()
    const platformHub = await sourceFile(new URL('../app/composables/usePlatformLogHub.ts', import.meta.url)).text()

    for (const source of [competitionHub, platformHub]) {
      expect(source).toContain('startRealtimeWithRetry(')
      expect(source).toContain('if (startPromise) return startPromise')
      expect(source).toContain('startAbortController?.abort()')
      expect(source).toContain('accessTokenFactory: getRealtimeAccessToken')
    }
    expect(competitionHub).toContain('subscribers.has(key) ? join(competitionId) : undefined')
    expect(competitionHub).toContain('if (!getAccessToken() && !connection) return')
    expect(platformHub).toContain('onScopeDispose(() =>')
  })
})
