
import { api, binaryResponse } from '../lib/api'


export function usePersonalWallpaper() {
  const { user } = useAuth()
  const wallpaperUrl = useState<string | null>('personal-wallpaper:url', () => null)
  const loadedRevision = useState<string | null>('personal-wallpaper:revision', () => null)
  const loadSequence = useState<number>('personal-wallpaper:load-sequence', () => 0)

  function clearWallpaper() {
    loadSequence.value += 1
    if (import.meta.client && wallpaperUrl.value)
      URL.revokeObjectURL(wallpaperUrl.value)
    wallpaperUrl.value = null
    loadedRevision.value = null
  }

  async function refreshWallpaper(force = false) {
    const revision = user.value?.wallpaperRevision ?? null
    if (!revision) {
      clearWallpaper()
      return
    }
    if (!force && loadedRevision.value === revision && wallpaperUrl.value)
      return

    const sequence = ++loadSequence.value
    try {
      let error: unknown;
      const data = await binaryResponse(responseOptions => api.api.v1.auth.me.wallpaper.get({ options: [...responseOptions] }), 'blob').catch(cause => { error = cause; return undefined });
      if (sequence !== loadSequence.value) return
      if (error || !(data instanceof Blob) || !data.type.startsWith('image/') || data.size === 0)
        throw error

      const nextUrl = URL.createObjectURL(data)
      if (wallpaperUrl.value)
        URL.revokeObjectURL(wallpaperUrl.value)
      wallpaperUrl.value = nextUrl
      loadedRevision.value = revision
    }
    catch {
      if (sequence === loadSequence.value)
        clearWallpaper()
    }
  }

  const wallpaperActive = computed(() =>
    Boolean(user.value?.wallpaperEnabled && wallpaperUrl.value))
  const wallpaperStyle = computed(() => wallpaperActive.value
    ? { '--personal-wallpaper': `url(${wallpaperUrl.value})` }
    : undefined)

  return {
    wallpaperUrl,
    wallpaperActive,
    wallpaperStyle,
    refreshWallpaper,
    clearWallpaper,
  }
}
