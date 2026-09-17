import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { createRuntimeOperationCoordinator } from '../app/lib/runtime-operation-coordinator'
import { RUNTIME_STOP_POLL_DELAYS_MS } from '../app/lib/runtime-stop-polling'

describe('runtime operation coordinator', () => {
  test('allows different instances to run concurrently and rejects duplicates for one instance', () => {
    const operations = createRuntimeOperationCoordinator()

    const first = operations.begin('runtime-a', 'reset')
    const duplicate = operations.begin('runtime-a', 'terminate')
    const second = operations.begin('runtime-b', 'terminate')

    expect(first).not.toBeNull()
    expect(duplicate).toBeNull()
    expect(second).not.toBeNull()
    expect(operations.pending.get('runtime-a')).toBe('reset')
    expect(operations.pending.get('runtime-b')).toBe('terminate')

    operations.cancelAll()
  })

  test('stops polling after the configured attempt bound', async () => {
    const operations = createRuntimeOperationCoordinator({
      maxAttempts: 3,
      timeoutMs: 10_000,
      wait: async () => undefined,
    })
    const token = operations.begin('runtime-a', 'start')!
    let probes = 0

    const result = await operations.poll(token, async () => {
      probes += 1
      return false
    })

    expect(result).toBe('exhausted')
    expect(probes).toBe(3)
    operations.finish(token)
    expect(operations.pending.has('runtime-a')).toBeFalse()
  })

  test('aborts a hung probe at the overall timeout', async () => {
    const operations = createRuntimeOperationCoordinator({ timeoutMs: 10 })
    const token = operations.begin('runtime-a', 'reset')!

    const result = await operations.poll(token, signal => new Promise<boolean>((resolve) => {
      signal.addEventListener('abort', () => resolve(false), { once: true })
    }))

    expect(result).toBe('exhausted')
    operations.finish(token)
  })

  test('uses the short stop schedule before returning to bounded backoff', async () => {
    const delays: number[] = []
    const operations = createRuntimeOperationCoordinator({
      maxAttempts: 8,
      intervalMs: 2_000,
      maxIntervalMs: 30_000,
      delaysMs: RUNTIME_STOP_POLL_DELAYS_MS,
      timeoutMs: 10_000,
      wait: async delay => { delays.push(delay) },
    })
    const token = operations.begin('runtime-a', 'terminate')!

    expect(await operations.poll(token, async () => false)).toBe('exhausted')
    expect(delays).toEqual([500, 1_000, 1_000, 2_000, 2_000, 3_000, 4_500])
    operations.finish(token)
  })

  test('cancels in-flight refreshes and clears pending state when the view leaves', async () => {
    const operations = createRuntimeOperationCoordinator({ timeoutMs: 10_000 })
    const token = operations.begin('runtime-a', 'force-terminate')!
    let probeStarted = false

    const polling = operations.poll(token, signal => new Promise<boolean>((resolve) => {
      probeStarted = true
      signal.addEventListener('abort', () => resolve(false), { once: true })
    }))

    await Promise.resolve()
    expect(probeStarted).toBeTrue()
    operations.cancelAll()

    expect(await polling).toBe('cancelled')
    expect(operations.pending.size).toBe(0)
    expect(operations.isActive(token)).toBeFalse()
    expect(operations.begin('runtime-b', 'start')).toBeNull()
  })
})

describe('runtime administration operation wiring', () => {
  test('keeps accepted operations in the background and only disables their target row', async () => {
    const source = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/runtimes.vue', import.meta.url),
    ).text()

    expect(source).not.toContain('const opPending')
    expect(source.match(/:disabled="isRuntimePending\(rt\)"/g)?.length).toBe(5)
    expect(source).toContain('refreshRuntimeInBackground(token, data?.runtimeInstanceId ?? rt.id')
    expect(source).not.toContain('await refreshRuntimeInBackground')
    expect(source).toContain('onBeforeUnmount(() => runtimeOperations.cancelAll())')
  })

  test('surfaces request failures without changing a runtime snapshot', async () => {
    const source = await sourceFile(
      new URL('../app/pages/admin/competitions/[id]/runtimes.vue', import.meta.url),
    ).text()

    expect(source.match(/const shouldNotify = runtimeOperations\.isActive\(token\)\s+runtimeOperations\.finish\(token\)\s+if \(shouldNotify\) toast\.error\(parseApiError\(e\)\.message\)/g)?.length).toBe(4)
    expect(source).toContain("markRuntimeStopping(rt.id)")
    expect(source).toContain("? { ...item, state: 'Stopping' }")
  })
})
