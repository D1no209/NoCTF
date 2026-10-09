import { computed, onMounted, onScopeDispose, ref, watch } from 'vue'
import { getLiveSoloProgram, manageLiveSoloViewer } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloProgramResponse as Program } from '~/api'
import { getAccessToken } from '~/lib/session'
import { parseLiveSoloError } from './live-solo-errors'
import { segmentIdFromUrl, rememberPublishedStates } from './program-state'
import { message, type UiMessage } from '~/utils/i18n'
import { matchStateKey, formatRoundClock } from './live-solo-state'
import { ProgramViewerLease } from './program-viewer-lease'
import { parseApiError } from '~/utils/api-error'

export function useLiveSoloProgram() {
  const route = useRoute(), { user } = useAuth()
  const competitionId = computed(() => route.params.id as string), matchId = computed(() => route.params.matchId as string)
  const program = ref<Program | null>(null), playingId = ref<string | null>(null), error = ref<UiMessage | null>(null), loading = ref(false)
  const admitted = ref(false), entering = ref(false)
  const publishedStates = ref<ReadonlyMap<string, NonNullable<NonNullable<Program['segments']>[number]['state']>>>(new Map())
  const state = computed(() => playingId.value ? publishedStates.value.get(playingId.value) ?? null : null)
  const source = computed(() => program.value?.programCaptureId ? program.value.playlistUrl ?? null : null)
  const stateKey = computed(() => matchStateKey(state.value?.matchState))
  const clock = computed(() => formatRoundClock(state.value?.limitSeconds == null ? null
    : Math.max(0, Math.ceil((state.value.limitSeconds * 1000 - (state.value.activeElapsedMilliseconds ?? 0)) / 1000))))
  let disposed = false, pending = false, request = 0, timer: ReturnType<typeof setTimeout> | undefined
  let renewedAt = 0
  let browserOperations = Promise.resolve()
  function viewer() {
    const path = { competitionId: competitionId.value, matchId: matchId.value }
    let expectedLeaseId: string | undefined
    return new ProgramViewerLease(async action => {
      const body = { action, expectedLeaseId }
      const operation = browserOperations.then(() => manageLiveSoloViewer({ path, body }))
      browserOperations = operation.then(() => undefined, () => undefined)
      const result = await operation
      if (action === 'Leave') return !result.error
      if (result.data) expectedLeaseId = result.data.leaseId
      if (!disposed && path.competitionId === competitionId.value && path.matchId === matchId.value && result.error) {
        const parsed = parseApiError(result.error, message('liveSolo.program.leaseExpired'))
        error.value = parsed.code === 'LiveSoloViewerCapacityReached' ? message('liveSolo.program.capacity')
          : parsed.code?.startsWith('LiveSoloViewer') ? message('liveSolo.program.leaseExpired') : parsed.displayMessage
      }
      return !result.error && !!result.data
    }, active => {
      admitted.value = active
      if (!active) { request++; program.value = null; playingId.value = null; publishedStates.value = new Map() }
    })
  }
  let lease = viewer()
  async function enter() {
    if (entering.value || disposed) return
    entering.value = true; error.value = null
    const current = lease
    try {
      const ok = await current.enter()
      if (current !== lease || disposed) return
      if (ok) { renewedAt = Date.now(); await load() }
      else if (!error.value) error.value = message('liveSolo.program.leaseExpired')
    }
    finally { if (current === lease && !disposed) entering.value = false }
  }
  function leave() { lease.leave(); error.value = null }
  function requestHeaders(): Record<string, string> { const token = getAccessToken(); return token ? { Authorization: `Bearer ${token}` } : {} }
  async function load() {
    if (pending || disposed || !admitted.value) return
    pending = true; const id = ++request
    try {
      const result = await getLiveSoloProgram({ path: { competitionId: competitionId.value, matchId: matchId.value } })
      if (disposed || id !== request) return
      if (result.response?.status === 404) { program.value = null; playingId.value = null; error.value = null; return }
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
      if (result.data.programCaptureId !== program.value?.programCaptureId) playingId.value = null
      publishedStates.value = rememberPublishedStates(publishedStates.value, result.data.segments ?? [], playingId.value)
      program.value = result.data; error.value = null
    }
    catch (cause) { if (!disposed && id === request) { program.value = null; playingId.value = null; error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage } }
    finally { pending = false; loading.value = false }
  }
  function fragment(url: string) { playingId.value = segmentIdFromUrl(url, window.location.origin) }
  function failed() { playingId.value = null; error.value = message('liveSolo.program.failed'); void load() }
  async function tick() {
    const current = lease
    if (admitted.value && Date.now() - renewedAt >= 5000) {
      const ok = await current.renew()
      if (current === lease && !disposed) {
        renewedAt = Date.now()
        if (!ok && !error.value) error.value = message('liveSolo.program.leaseExpired')
      }
    }
    await load(); if (!disposed) timer = setTimeout(tick, 2000)
  }
  watch([competitionId, matchId, () => user.value?.userId], () => {
    const hadSession = admitted.value || entering.value || error.value != null
    lease.dispose(); lease = viewer(); request++; entering.value = false
    error.value = hadSession ? message('liveSolo.program.leaseExpired') : null
  })
  onMounted(() => { void tick() })
  onScopeDispose(() => { disposed = true; lease.dispose(); request++; if (timer) clearTimeout(timer) })
  return { program, playingId, state, source, stateKey, clock, loading, error, load, fragment, failed, requestHeaders, admitted, entering, enter, leave }
}
export type LiveSoloProgramViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloProgram>>
