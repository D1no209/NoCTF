import { describe, expect, test } from 'bun:test'
import { createLatestPageRefresh, createTrailingRefresh } from '../app/lib/latest-page-refresh'

function deferred() {
  let resolve!: () => void
  const promise = new Promise<void>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

describe('createLatestPageRefresh', () => {
  test('resets and loads the first page', async () => {
    const calls: string[] = []
    const latest = createLatestPageRefresh({
      loadMore: async () => { calls.push('load') },
      reset: () => calls.push('reset'),
    })

    await latest.refreshLatest()

    expect(calls).toEqual(['reset', 'load'])
  })

  test('coalesces repeated live events into one final reload', async () => {
    const firstLoad = deferred()
    let loads = 0
    let resets = 0
    const latest = createLatestPageRefresh({
      loadMore: async () => {
        loads += 1
        if (loads === 1) await firstLoad.promise
      },
      reset: () => { resets += 1 },
    })

    const firstRefresh = latest.refreshLatest()
    await Promise.resolve()
    const secondRefresh = latest.refreshLatest()
    const thirdRefresh = latest.refreshLatest()

    expect(secondRefresh).toBe(firstRefresh)
    expect(thirdRefresh).toBe(firstRefresh)
    expect(loads).toBe(1)

    firstLoad.resolve()
    await firstRefresh

    expect(resets).toBe(2)
    expect(loads).toBe(2)
  })

  test('waits for load-more before resetting to the newest page', async () => {
    const paginationLoad = deferred()
    const calls: string[] = []
    let loads = 0
    const latest = createLatestPageRefresh({
      loadMore: async () => {
        loads += 1
        calls.push(`load-${loads}`)
        if (loads === 1) await paginationLoad.promise
      },
      reset: () => calls.push('reset'),
    })

    const loadingMore = latest.loadNextPage()
    await Promise.resolve()
    const refreshing = latest.refreshLatest()

    expect(calls).toEqual(['load-1'])
    paginationLoad.resolve()
    await Promise.all([loadingMore, refreshing])

    expect(calls).toEqual(['load-1', 'reset', 'load-2'])
  })

  test('starts a new reload after the previous refresh completed', async () => {
    let loads = 0
    const latest = createLatestPageRefresh({
      loadMore: async () => { loads += 1 },
      reset: () => undefined,
    })

    await latest.refreshLatest()
    await latest.refreshLatest()

    expect(loads).toBe(2)
  })
})

describe('createTrailingRefresh', () => {
  test('runs one final refresh when live invalidations arrive in flight', async () => {
    const firstRun = deferred()
    let runs = 0
    const refresh = createTrailingRefresh(async () => {
      runs += 1
      if (runs === 1) await firstRun.promise
    })

    const firstRefresh = refresh()
    await Promise.resolve()
    const secondRefresh = refresh()
    const thirdRefresh = refresh()

    expect(secondRefresh).toBe(firstRefresh)
    expect(thirdRefresh).toBe(firstRefresh)
    expect(runs).toBe(1)

    firstRun.resolve()
    await firstRefresh

    expect(runs).toBe(2)
  })
})
