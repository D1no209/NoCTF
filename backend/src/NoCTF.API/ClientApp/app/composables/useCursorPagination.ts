import { computed, ref } from 'vue'
import type { Ref } from 'vue'
import { parseApiError } from '../utils/api-error'
import type { ApiError } from '../utils/api-error'

export interface CursorPage<T> {
  items?: T[] | null
  nextCursor?: string | null
}

export interface CursorResetOptions {
  preserveItems?: boolean
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
  let generation = 0
  let loadingGeneration: number | null = null
  let replaceOnNextPage = false

  const hasMore = computed(() => initialized.value && nextCursor.value !== null)

  async function loadMore(): Promise<void> {
    const requestGeneration = generation
    if (loadingGeneration === requestGeneration) return
    if (initialized.value && nextCursor.value === null) return

    loadingGeneration = requestGeneration
    loading.value = true
    error.value = null
    const cursor = nextCursor.value
    try {
      const page = await fetcher(cursor)
      if (requestGeneration !== generation) return
      if (replaceOnNextPage && cursor === null) {
        items.value = page.items ?? []
        replaceOnNextPage = false
      }
      else {
        items.value.push(...(page.items ?? []))
      }
      nextCursor.value = page.nextCursor ?? null
      initialized.value = true
    }
    catch (e) {
      if (requestGeneration === generation)
        error.value = parseApiError(e)
    }
    finally {
      if (loadingGeneration !== requestGeneration) return
      loadingGeneration = null
      if (requestGeneration === generation)
        loading.value = false
    }
  }

  function reset(options: CursorResetOptions = {}): void {
    generation += 1
    loadingGeneration = null
    replaceOnNextPage = options.preserveItems === true && items.value.length > 0
    if (!options.preserveItems)
      items.value = []
    nextCursor.value = null
    loading.value = false
    error.value = null
    initialized.value = false
  }

  return { items, nextCursor, loading, error, hasMore, initialized, loadMore, reset }
}
