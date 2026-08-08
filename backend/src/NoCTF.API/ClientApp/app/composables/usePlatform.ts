import { platformConfigurationGet } from '~/api'

/** Platform branding (name/description/logo), loaded once for the whole app. */
export function usePlatform() {
  const configuration = useState<Awaited<ReturnType<typeof load>> | null>('platform:configuration', () => null)

  async function load() {
    const { data } = await platformConfigurationGet()
    return data ?? null
  }

  async function ensureLoaded(): Promise<void> {
    if (configuration.value) return
    configuration.value = await load().catch(() => null)
  }

  return { configuration, ensureLoaded }
}
