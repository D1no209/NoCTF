import { describe, expect, test } from 'bun:test'
import type { CursorPage } from '../app/composables/useCursorPagination'
import { useCursorPagination } from '../app/composables/useCursorPagination'

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, reject, resolve }
}

describe('cursor pagination request generations', () => {
  test('ignores a stale response after reset and starts the new first page immediately', async () => {
    const first = deferred<CursorPage<string>>()
    const second = deferred<CursorPage<string>>()
    const cursors: Array<string | null> = []
    let calls = 0
    const pagination = useCursorPagination<string>((cursor) => {
      cursors.push(cursor)
      calls += 1
      return calls === 1 ? first.promise : second.promise
    })

    const oldLoad = pagination.loadMore()
    await Promise.resolve()
    pagination.reset()
    const newLoad = pagination.loadMore()

    expect(cursors).toEqual([null, null])
    expect(pagination.loading.value).toBeTrue()

    second.resolve({ items: ['new'], nextCursor: null })
    await newLoad
    expect(pagination.items.value).toEqual(['new'])
    expect(pagination.loading.value).toBeFalse()

    first.resolve({ items: ['stale'], nextCursor: 'stale-cursor' })
    await oldLoad
    expect(pagination.items.value).toEqual(['new'])
    expect(pagination.nextCursor.value).toBeNull()
  })

  test('ignores an error from an invalidated request', async () => {
    const oldRequest = deferred<CursorPage<string>>()
    const pagination = useCursorPagination<string>(() => oldRequest.promise)

    const loading = pagination.loadMore()
    pagination.reset()
    oldRequest.reject(new Error('stale failure'))
    await loading

    expect(pagination.error.value).toBeNull()
    expect(pagination.initialized.value).toBeFalse()
  })

  test('deduplicates a current load and does not refetch a terminal page', async () => {
    const request = deferred<CursorPage<string>>()
    let calls = 0
    const pagination = useCursorPagination<string>(() => {
      calls += 1
      return request.promise
    })

    const first = pagination.loadMore()
    await pagination.loadMore()
    expect(calls).toBe(1)

    request.resolve({ items: ['only'], nextCursor: null })
    await first
    await pagination.loadMore()

    expect(calls).toBe(1)
    expect(pagination.items.value).toEqual(['only'])
  })

  test('keeps the last successful page visible when a later request fails', async () => {
    let calls = 0
    const pagination = useCursorPagination<string>(async () => {
      calls += 1
      if (calls === 1) return { items: ['kept'], nextCursor: 'next' }
      throw new Error('next page failed')
    })

    await pagination.loadMore()
    await pagination.loadMore()

    expect(pagination.items.value).toEqual(['kept'])
    expect(pagination.nextCursor.value).toBe('next')
    expect(pagination.initialized.value).toBeTrue()
    expect(pagination.error.value).not.toBeNull()
  })

  test('preserves the prior result across a failed filtered reload and replaces it on success', async () => {
    let phase: 'initial' | 'failure' | 'success' = 'initial'
    const pagination = useCursorPagination<string>(async () => {
      if (phase === 'initial') return { items: ['old'], nextCursor: null }
      if (phase === 'failure') throw new Error('reload failed')
      return { items: ['new'], nextCursor: null }
    })

    await pagination.loadMore()
    phase = 'failure'
    pagination.reset({ preserveItems: true })
    await pagination.loadMore()

    expect(pagination.items.value).toEqual(['old'])
    expect(pagination.error.value).not.toBeNull()

    phase = 'success'
    await pagination.loadMore()

    expect(pagination.items.value).toEqual(['new'])
    expect(pagination.error.value).toBeNull()
  })
})
