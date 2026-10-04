
import { ResponseMetadata, api, RequestPolicyOption, binaryResponse } from '../../lib/api'
import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'


/** Loads one competition's mutable poster without retaining stale browser responses. */
export function useCompetitionPoster(competitionId: string) {
  const posterUrl = ref<string | null>(null)
  const posterLoading = ref(true)
  const posterError = ref<UiMessage | null>(null)
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
      let error: unknown;
      const response = new ResponseMetadata();
      const data = await binaryResponse(responseOptions => api.api.v1.competitions.byCompetitionId(competitionId).poster.get({ options: [new RequestPolicyOption({ response: response, cache: 'no-store' }), ...responseOptions] }), 'blob').catch(cause => { error = cause; return undefined });
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
        posterError.value = parseApiError(error, describeMessage('competitions.competitionPoster.error.loadCompetitionPosterFailed')).displayMessage
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
