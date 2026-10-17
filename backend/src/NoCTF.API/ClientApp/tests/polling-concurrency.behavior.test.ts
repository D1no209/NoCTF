import { expect, test } from 'bun:test'
import { effectScope } from 'vue'
import { usePolling } from '../app/composables/usePolling'

const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

function deferred() {
  let resolve!: () => void
  const promise = new Promise<void>((done) => { resolve = done })
  return { promise, resolve }
}

async function waitFor(condition: () => boolean, timeoutMs = 2_000): Promise<void> {
  const started = Date.now()
  while (!condition()) {
    if (Date.now() - started > timeoutMs) throw new Error('condition timed out')
    await sleep(2)
  }
}

test('restarting while a probe is in flight leaves exactly one chain scheduling', async () => {
  const scope = effectScope()
  let probes = 0
  // probe 阻塞在闸门上,使「谁还在排程下一次探测」可被精确计数。
  let gate = deferred()
  const polling = scope.run(() => usePolling(async () => {
    probes += 1
    await gate.promise
    return false
  }, { interval: 10, maxInterval: 10, timeout: 10_000 }))!

  polling.start()
  await waitFor(() => probes === 1)
  // 旧实现在这里复位 stopped,正在 await 的旧链会继续排程并与新链并存。
  polling.start()
  await waitFor(() => probes === 2)

  const before = probes
  const released = gate
  gate = deferred()
  released.resolve()
  await sleep(120) // 给两条链各自排程下一次探测的机会
  polling.stop()
  scope.stop()

  // 修复后只有新链会再次探测;旧实现会多出一次(两条链各排一次)。
  expect(probes - before).toBe(1)
})

test('stop() ends the chain and no probe runs afterwards', async () => {
  const scope = effectScope()
  let probes = 0
  const polling = scope.run(() => usePolling(async () => {
    probes += 1
    await sleep(10)
    return false
  }, { interval: 10, maxInterval: 10, timeout: 10_000 }))!

  polling.start()
  await sleep(25)
  polling.stop()
  const settled = probes
  await sleep(80)

  expect(polling.polling.value).toBe(false)
  expect(probes).toBe(settled)
  scope.stop()
})

test('a start() after scope disposal never arms a new chain', async () => {
  const scope = effectScope()
  let probes = 0
  const polling = scope.run(() => usePolling(async () => {
    probes += 1
    return false
  }, { interval: 10, timeout: 10_000 }))!

  scope.stop()
  polling.start()
  await sleep(80)

  expect(probes).toBe(0)
  expect(polling.polling.value).toBe(false)
})
