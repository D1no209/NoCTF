import { getNavigationCapabilitiesEndpoint } from '../api'
import { watchNotifications } from './useNotificationHub'

export function useNavigationCapabilities(listen = true) {
  const { user } = useAuth()
  const result = useAsyncData(
    computed(() => `navigation-capabilities:${user.value?.userId ?? 'anonymous'}`),
    async () => {
      if (!user.value) return { canAccessTheoryBank: false, hasManagedCompetitions: false }
      const { data, error } = await getNavigationCapabilitiesEndpoint()
      if (error || !data) throw error ?? new Error('Navigation capabilities response is missing.')
      return data
    },
    { getCachedData: (key, app) => app.payload.data[key] },
  )
  const refresh = async () => { await result.refresh({ dedupe: 'defer' }) }
  if (listen) {
    let unwatch: (() => void) | undefined
    const onFocus = () => { void refresh() }
    onMounted(() => {
      window.addEventListener('focus', onFocus)
      unwatch = watchNotifications({
        navigationCapabilitiesChanged: onFocus,
        onReconnected: onFocus,
      })
    })
    onBeforeUnmount(() => { window.removeEventListener('focus', onFocus); unwatch?.() })
  }
  return { capabilities: result.data, error: result.error, refresh }
}
