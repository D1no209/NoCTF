import { computed, onScopeDispose, shallowRef, ref, watch } from 'vue'
import { adminRouteId, validAdminId } from './admin-navigation'
import { createLatestRequestGuard } from '~/lib/latest-request'
import { parseApiError } from '~/utils/api-error'
import { translate } from '~/utils/i18n'

/** Route selection owns the sheet; detail reads are independent of list pagination. */
export function useAdminDetailRoute<T>(parameter: string, base: string, load: (id: string, signal: AbortSignal) => Promise<T>) {
  const route = useRoute()
  const router = useRouter()
  const selectedId = computed(() => adminRouteId(route.params[parameter]))
  const data = shallowRef<T | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)
  const requests = createLatestRequestGuard()
  let controller: AbortController | undefined

  function select(id: string) {
    return router.push({ path: `${base}/${encodeURIComponent(id)}`, query: route.query, hash: route.hash })
  }
  function close(open: boolean) {
    if (!open && selectedId.value) void router.push({ path: base, query: route.query, hash: route.hash })
  }
  const open = computed({ get: () => selectedId.value !== null, set: close })

  watch(selectedId, async (id) => {
    controller?.abort()
    const request = requests.begin()
    data.value = null
    error.value = null
    loading.value = false
    if (!id) return
    if (!validAdminId(id)) {
      error.value = translate('adminNavigation.invalidId')
      return
    }
    controller = new AbortController()
    loading.value = true
    try {
      const result = await load(id, controller.signal)
      if (requests.isCurrent(request)) data.value = result
    }
    catch (failure) {
      if (requests.isCurrent(request)) error.value = parseApiError(failure, translate('adminNavigation.detailFailed')).message
    }
    finally {
      if (requests.isCurrent(request)) loading.value = false
    }
  }, { immediate: true, flush: 'sync' })
  onScopeDispose(() => { requests.invalidate(); controller?.abort(); data.value = null })
  return { selectedId, data, loading, error, open, select, close }
}
