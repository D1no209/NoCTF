import { competitionPosterGet } from '../../api'

/** Loads one competition's mutable poster without retaining stale browser responses. */
export function useCompetitionPoster(competitionId: string) {
  const posterUrl = ref<string | null>(null)
  const posterLoading = ref(true)
  const posterError = ref<string | null>(null)
  let generation = 0

  function clearPoster(): void {
    if (import.meta.client && posterUrl.value)
      URL.revokeObjectURL(posterUrl.value)
    posterUrl.value = null
  }

  function cancelPoster(): void {
    generation += 1
    clearPoster()
    posterLoading.value = false
    posterError.value = null
  }

  async function refreshPoster(): Promise<void> {
    const current = ++generation
    posterLoading.value = true
    posterError.value = null
    try {
      const { data, error, response } = await competitionPosterGet({
        path: { competitionId },
        parseAs: 'blob',
        cache: 'no-store',
      })
      if (current !== generation) return
      if (response?.status === 404) {
        clearPoster()
        return
      }
      if (error || !(data instanceof Blob) || !data.type.startsWith('image/') || data.size === 0)
        throw error

      const nextUrl = URL.createObjectURL(data)
      clearPoster()
      posterUrl.value = nextUrl
    }
    catch (error) {
      if (current === generation)
        posterError.value = parseApiError(error, translate('ui.failedToLoadCompetitionPoster')).message
    }
    finally {
      if (current === generation) posterLoading.value = false
    }
  }

  onUnmounted(() => {
    cancelPoster()
  })

  return {
    posterUrl,
    posterLoading,
    posterError,
    refreshPoster,
    clearPoster,
    cancelPoster,
  }
}
