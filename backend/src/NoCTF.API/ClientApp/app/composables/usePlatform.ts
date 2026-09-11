import { platformConfigurationGet } from '../api'

let platformLoadPromise: Promise<void> | null = null

/** Public branding and deployment capabilities, loaded once for the whole app. */
export function usePlatform() {
  const configuration = useState<Awaited<ReturnType<typeof load>> | null>('platform:configuration', () => null)
  const error = useState<string | null>('platform:configuration-error', () => null)
  const loading = useState('platform:configuration-loading', () => false)

  async function load() {
    const { data, error: requestError } = await platformConfigurationGet()
    if (requestError || !data)
      throw parseApiError(requestError, translate("ui.failedToLoadPlatformConfiguration"))
    return data ?? null
  }

  async function refresh(): Promise<void> {
    platformLoadPromise ??= (async () => {
      loading.value = true
      try {
        configuration.value = await load()
        error.value = null
      }
      catch (requestError) {
        configuration.value = null
        error.value = parseApiError(requestError, translate("ui.failedToLoadPlatformConfiguration")).message
      }
      finally {
        loading.value = false
        platformLoadPromise = null
      }
    })()
    await platformLoadPromise
  }

  async function ensureLoaded(): Promise<void> {
    if (configuration.value) return
    await refresh()
  }

  return { configuration, error, loading, ensureLoaded, refresh }
}
