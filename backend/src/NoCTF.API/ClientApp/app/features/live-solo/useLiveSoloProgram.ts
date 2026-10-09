import { computed, onMounted, onScopeDispose, ref } from 'vue'
import { getLiveSoloProgram } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloProgramResponse as Program } from '~/api'
import { getAccessToken } from '~/lib/session'
import { parseLiveSoloError } from './live-solo-errors'
import { segmentIdFromUrl, stateForPlayingSegment } from './program-state'
import { message, type UiMessage } from '~/utils/i18n'
import { matchStateKey, formatRoundClock } from './live-solo-state'

export function useLiveSoloProgram() {
  const route = useRoute()
  const competitionId = computed(() => route.params.id as string), matchId = computed(() => route.params.matchId as string)
  const program = ref<Program | null>(null), playingId = ref<string | null>(null), error = ref<UiMessage | null>(null), loading = ref(true)
  const state = computed(() => stateForPlayingSegment(program.value?.segments ?? [], playingId.value))
  const source = computed(() => program.value?.programCaptureId ? program.value.playlistUrl ?? null : null)
  const stateKey = computed(() => matchStateKey(state.value?.matchState))
  const clock = computed(() => formatRoundClock(state.value?.limitSeconds == null ? null
    : Math.max(0, Math.ceil((state.value.limitSeconds * 1000 - (state.value.activeElapsedMilliseconds ?? 0)) / 1000))))
  let disposed = false, pending = false, request = 0, timer: ReturnType<typeof setTimeout> | undefined
  function requestHeaders(): Record<string, string> { const token = getAccessToken(); return token ? { Authorization: `Bearer ${token}` } : {} }
  async function load() {
    if (pending || disposed) return
    pending = true; const id = ++request
    try {
      const result = await getLiveSoloProgram({ path: { competitionId: competitionId.value, matchId: matchId.value } })
      if (disposed || id !== request) return
      if (result.response?.status === 404) { program.value = null; playingId.value = null; error.value = null; return }
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
      if (result.data.programCaptureId !== program.value?.programCaptureId) playingId.value = null
      program.value = result.data; error.value = null
    }
    catch (cause) { if (!disposed && id === request) { program.value = null; playingId.value = null; error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage } }
    finally { pending = false; loading.value = false }
  }
  function fragment(url: string) { playingId.value = segmentIdFromUrl(url, window.location.origin) }
  function failed() { playingId.value = null; error.value = message('liveSolo.program.failed'); void load() }
  async function tick() { await load(); if (!disposed) timer = setTimeout(tick, 2000) }
  onMounted(() => { void tick() })
  onScopeDispose(() => { disposed = true; request++; if (timer) clearTimeout(timer) })
  return { program, playingId, state, source, stateKey, clock, loading, error, load, fragment, failed, requestHeaders }
}
export type LiveSoloProgramViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloProgram>>
