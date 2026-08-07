export interface CursorPage<T> {
  items?: T[] | null
  nextCursor?: string | null
}

/**
 * Signed-keyset "load more" pagination.
 * The cursor is bound to endpoint + filters by the backend, so callers must
 * call `reset()` whenever filters change and never reuse a stale cursor.
 */
export function useCursorPagination<T>(fetcher: (cursor: string | null) => Promise<CursorPage<T>>) {
  const items = ref<T[]>([]) as Ref<T[]>
  const nextCursor = ref<string | null>(null)
  const loading = ref(false)
  const error = ref<ApiError | null>(null)
  const initialized = ref(false)

  const hasMore = computed(() => initialized.value && nextCursor.value !== null)

  async function loadMore(): Promise<void> {
    if (loading.value) return
    loading.value = true
    error.value = null
    try {
      const page = await fetcher(nextCursor.value)
      items.value.push(...(page.items ?? []))
      nextCursor.value = page.nextCursor ?? null
      initialized.value = true
    }
    catch (e) {
      error.value = parseApiError(e)
    }
    finally {
      loading.value = false
    }
  }

  function reset(): void {
    items.value = []
    nextCursor.value = null
    error.value = null
    initialized.value = false
  }

  return { items, nextCursor, loading, error, hasMore, initialized, loadMore, reset }
}
