import { computed, ref } from 'vue'
import type { Ref } from 'vue'
import type { CursorPage, CursorResetOptions } from './useCursorPagination'
import { parseApiError } from '../utils/api-error'
import type { ApiError } from '../utils/api-error'

/** Previous/next navigation over opaque cursors; retains positions, not historical rows. */
export function useCursorPagePagination<T>(
  fetcher: (cursor: string | null, limit: number) => Promise<CursorPage<T>>,
  initialLimit = 50,
) {
  const items = ref<T[]>([]) as Ref<T[]>
  const page = ref(1)
  const limit = ref(initialLimit)
  const loading = ref(false)
  const initialized = ref(false)
  const error = ref<ApiError | null>(null)
  const positions = ref<(string | null)[]>([null])
  let generation = 0
  let loadingGeneration: number | null = null
  const hasPrevious = computed(() => page.value > 1)
  const hasNext = computed(() => positions.value[page.value] != null)

  async function loadPage(target = page.value): Promise<void> {
    if (!Number.isInteger(target) || target < 1 || target > positions.value.length
      || (target > 1 && positions.value[target - 1] == null)) return
    const requestGeneration = generation
    if (loadingGeneration === requestGeneration) return
    loadingGeneration = requestGeneration
    loading.value = true
    error.value = null
    try {
      const result = await fetcher(positions.value[target - 1] ?? null, limit.value)
      if (requestGeneration !== generation) return
      items.value = result.items ?? []
      page.value = target
      positions.value.splice(target, positions.value.length - target,
        ...(result.nextCursor ? [result.nextCursor] : []))
      initialized.value = true
    }
    catch (value) {
      if (requestGeneration === generation) error.value = parseApiError(value)
    }
    finally {
      if (loadingGeneration === requestGeneration) {
        loadingGeneration = null
        if (requestGeneration === generation) loading.value = false
      }
    }
  }

  function reset(options: CursorResetOptions = {}): void {
    generation += 1
    loadingGeneration = null
    if (!options.preserveItems) items.value = []
    positions.value = [null]
    page.value = 1
    loading.value = false
    initialized.value = false
    error.value = null
  }

  return { items, page, limit, loading, initialized, error, hasPrevious, hasNext, loadPage, reset }
}
