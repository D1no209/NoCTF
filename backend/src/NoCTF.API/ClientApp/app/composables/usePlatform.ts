import { platformConfigurationGet } from '~/api'

/** Platform branding (name/description/logo), loaded once for the whole app. */
export function usePlatform() {
  const configuration = useState<Awaited<ReturnType<typeof load>> | null>('platform:configuration', () => null)
  const error = useState<string | null>('platform:configuration-error', () => null)
  const loading = useState('platform:configuration-loading', () => false)

  async function load() {
    const { data, error: requestError } = await platformConfigurationGet()
    if (requestError || !data)
      throw parseApiError(requestError, translate('加载平台配置失败'))
    return data ?? null
  }

  async function ensureLoaded(): Promise<void> {
    if (configuration.value || loading.value) return
    loading.value = true
    try {
      configuration.value = await load()
      error.value = null
    }
    catch (requestError) {
      error.value = parseApiError(requestError, translate('加载平台配置失败')).message
    }
    finally {
      loading.value = false
    }
  }

  return { configuration, error, loading, ensureLoaded }
}
