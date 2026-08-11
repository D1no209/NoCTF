export interface LatestPageRefreshOptions {
  loadMore: () => Promise<void>
  reset: () => void
}

/**
 * Coalesces live invalidations while guaranteeing one trailing refresh after
 * an in-flight request. The callback owns its error presentation so a failed
 * refresh can preserve the currently rendered state.
 */
export function createTrailingRefresh(run: () => Promise<void>) {
  let refresh: Promise<void> | null = null
  let refreshRequested = false

  return function refreshTrailing(): Promise<void> {
    refreshRequested = true
    if (refresh) return refresh

    refresh = (async () => {
      do {
        refreshRequested = false
        await run()
      } while (refreshRequested)
    })().finally(() => {
      refresh = null
    })

    return refresh
  }
}

/**
 * Serializes pagination and first-page refreshes. Repeated refresh requests
 * received during an active request are coalesced into one final reload so the
 * caller always settles on the newest first page without racing load-more.
 */
export function createLatestPageRefresh(options: LatestPageRefreshOptions) {
  let pageLoad: Promise<void> | null = null
  let refresh: Promise<void> | null = null
  let refreshRequested = false

  function loadNextPage(): Promise<void> {
    if (pageLoad) return pageLoad

    pageLoad = Promise.resolve()
      .then(options.loadMore)
      .finally(() => {
        pageLoad = null
      })
    return pageLoad
  }

  function refreshLatest(): Promise<void> {
    refreshRequested = true
    if (refresh) return refresh

    refresh = (async () => {
      do {
        refreshRequested = false
        if (pageLoad) await pageLoad
        options.reset()
        await loadNextPage()
      } while (refreshRequested)
    })().finally(() => {
      refresh = null
    })

    return refresh
  }

  return { loadNextPage, refreshLatest }
}
