import { expect, test } from 'bun:test'
import { effectScope } from 'vue'
import { useOffsetPagination } from '../app/composables/useOffsetPagination'

const drain = () => new Promise(resolve => setTimeout(resolve, 0))

test('a page request issued during an in-flight load is applied afterwards', async () => {
  const scope = effectScope()
  const requested: number[] = []
  let release!: () => void
  const gate = new Promise<void>(resolve => { release = resolve })
  const pagination = scope.run(() => useOffsetPagination<number>(async ({ offset, limit }) => {
    requested.push(offset / limit + 1)
    if (requested.length === 1) await gate
    return { items: [1], total: 100 }
  }, { initialPageSize: 10 }))!

  const first = pagination.loadPage(2)
  await drain()
  // 旧实现会直接丢弃这次请求,页码停留在 2。
  const second = pagination.loadPage(3)
  release()
  await first
  await second
  await drain()

  expect(requested).toEqual([2, 3])
  expect(pagination.page.value).toBe(3)
  scope.stop()
})

test('a trailing page request is dropped once filters reset the list', async () => {
  const scope = effectScope()
  const requested: number[] = []
  let release!: () => void
  const gate = new Promise<void>(resolve => { release = resolve })
  const pagination = scope.run(() => useOffsetPagination<number>(async ({ offset, limit }) => {
    requested.push(offset / limit + 1)
    if (requested.length === 1) await gate
    return { items: [1], total: 100 }
  }, { initialPageSize: 10 }))!

  const first = pagination.loadPage(2)
  await drain()
  pagination.loadPage(3)
  pagination.reset()
  release()
  await first
  await drain()

  expect(requested).toEqual([2])
  expect(pagination.page.value).toBe(1)
  scope.stop()
})
