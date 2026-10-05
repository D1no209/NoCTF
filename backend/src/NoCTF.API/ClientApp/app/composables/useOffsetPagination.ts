import { computed, ref } from 'vue'
import type { Ref } from 'vue'

import { parseApiError } from '../utils/api-error'
import type { ApiError } from '../utils/api-error'

export interface OffsetPage<T> {
  items?: T[] | null
  total?: number | null
}

export interface OffsetPageRequest {
  offset: number
  limit: number
  desc: boolean
}

export interface OffsetPaginationOptions {
  initialPageSize?: number
  initialDesc?: boolean
}

/** Offset/total pagination for replace-in-place list views. */
export function useOffsetPagination<T>(
  fetcher: (request: OffsetPageRequest) => Promise<OffsetPage<T>>,
  options: OffsetPaginationOptions = {},
) {
  const page = ref(1)
  const limit = ref(options.initialPageSize ?? 10)
  const desc = ref(options.initialDesc ?? false)
  const total = ref(0)
  const items = ref<T[]>([]) as Ref<T[]>
  const loading = ref(false)
  const error = ref<ApiError | null>(null)
  const initialized = ref(false)
  let generation = 0
  let loadingGeneration: number | null = null

  const offset = computed(() => (page.value - 1) * limit.value)
  const pageCount = computed(() => Math.max(1, Math.ceil(total.value / limit.value)))
  const hasPrevious = computed(() => page.value > 1)
  const hasNext = computed(() => page.value < pageCount.value)

  async function loadPage(targetPage = page.value): Promise<void> {
    const target = Math.max(1, targetPage)
    const requestGeneration = generation
    if (loadingGeneration === requestGeneration) return

    page.value = target
    loadingGeneration = requestGeneration
    loading.value = true
    error.value = null
    try {
      const result = await fetcher({ offset: (target - 1) * limit.value, limit: limit.value, desc: desc.value })
      if (requestGeneration !== generation) return
      total.value = Math.max(0, result.total ?? 0)
      const lastPage = Math.max(1, Math.ceil(total.value / limit.value))
      if (target > lastPage) {
        page.value = lastPage
        loadingGeneration = null
        await loadPage(lastPage)
        return
      }
      page.value = target
      items.value = result.items ?? []
      initialized.value = true
    }
    catch (value) {
      if (requestGeneration === generation)
        error.value = parseApiError(value)
    }
    finally {
      if (loadingGeneration === requestGeneration) {
        loadingGeneration = null
        if (requestGeneration === generation) loading.value = false
      }
    }
  }

  function reset(): void {
    generation += 1
    loadingGeneration = null
    page.value = 1
    total.value = 0
    items.value = []
    loading.value = false
    error.value = null
    initialized.value = false
  }

  async function setPageSize(value: number): Promise<void> {
    if (!Number.isFinite(value) || value < 1 || value === limit.value) return
    limit.value = Math.floor(value)
    reset()
    await loadPage(1)
  }

  async function setDescending(value: boolean): Promise<void> {
    if (desc.value === value) return
    desc.value = value
    reset()
    await loadPage(1)
  }

  return {
    page,
    limit,
    desc,
    offset,
    total,
    items,
    loading,
    error,
    initialized,
    pageCount,
    hasPrevious,
    hasNext,
    loadPage,
    reset,
    setPageSize,
    setDescending,
  }
}
