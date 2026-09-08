import { proxyRefs } from 'vue'
import { toRefs } from 'vue'

import { Lightbulb, LockKeyhole } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { getChallengeEndpoint, getGameplayFactStatusEndpoint, unlockChallengeHintEndpoint } from '../../api'
import type { NoCtfapiEndpointsChallengesParticipantChallengeHintResponse } from '../../api'
import { affectsChallengeHints, hintUnlockState, readableHintContent } from '../../lib/challenge-hints'
import { createLatestRequestGuard } from '../../lib/latest-request'
import { createTrailingRefresh } from '../../lib/latest-page-refresh'

type Hint = NoCtfapiEndpointsChallengesParticipantChallengeHintResponse

type Events = { unlocked: [] }

/** Owns state, effects and commands for ChallengeHints. */
export function useChallengeHints(props: Readonly<{
  competitionId: string
  competitionChallengeId: string
  hints?: Hint[] | null
}>,
emit: { (event: "unlocked", ...args: []): void }) {
  const { isLoggedIn } = useAuth()

  const headingId = useId()

  const hints = ref<Hint[]>(props.hints ?? [])

  const refreshing = ref(false)

  const refreshError = ref<string | null>(null)

  const unlockError = ref<string | null>(null)

  const confirmingId = ref<string | null>(null)

  const submitting = ref(false)

  const pendingFactId = ref<string | null>(null)

  const requestGuard = createLatestRequestGuard()

  const busy = computed(() => submitting.value || pendingFactId.value !== null)

  let disposed = false

  watch(() => props.hints, value => { hints.value = value ?? [] })

  async function loadHints(): Promise<void> {
    const request = requestGuard.begin()
    refreshing.value = true
    try {
      const { data, error } = await getChallengeEndpoint({
        path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      })
      if (!requestGuard.isCurrent(request)) return
      if (error || !data) throw parseApiError(error, translate("ui.failedToRefreshHints"))
      hints.value = data.hints ?? []
      refreshError.value = null
    }
    catch (error) {
      if (requestGuard.isCurrent(request)) refreshError.value = parseApiError(error, translate("ui.failedToRefreshHints")).message
    }
    finally {
      if (requestGuard.isCurrent(request)) refreshing.value = false
    }
  }

  const refreshHints = createTrailingRefresh(loadHints)

  async function checkUnlock(): Promise<boolean> {
    const id = pendingFactId.value
    if (!id) return true
    const { data, error } = await getGameplayFactStatusEndpoint({
      path: { competitionId: props.competitionId, gameplayFactId: id },
    })
    if (disposed || id !== pendingFactId.value) return true
    if (error || !data) throw parseApiError(error, translate("ui.failedToRefreshHintUnlockStatus"))
    const state = hintUnlockState(data)
    if (state === 'pending') return false
    pendingFactId.value = null
    if (state === 'unlocked') {
      await refreshHints()
      if (disposed) return true
      toast.success(translate("ui.hintUnlocked"))
      emit('unlocked')
    }
    else {
      unlockError.value = data.failureCode
        ? gameplayFactFailureCodeLabel(data.failureCode)
        : translate("ui.failedToUnlockHint")
    }
    return true
  }

  const { start, stop, error: pollingError, timedOut } = usePolling(checkUnlock, { timeout: 45_000 })

  async function unlock(hint: Hint): Promise<void> {
    if (!hint.id || !hint.canUnlock || busy.value || confirmingId.value !== hint.id) return
    submitting.value = true
    unlockError.value = null
    try {
      const { data, error } = await unlockChallengeHintEndpoint({
        path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId, hintId: hint.id },
      })
      if (disposed) return
      if (error || !data?.gameplayFactId) throw parseApiError(error, translate("ui.failedToUnlockHint"))
      confirmingId.value = null
      pendingFactId.value = data.gameplayFactId
      start()
    }
    catch (error) {
      if (!disposed) unlockError.value = parseApiError(error, translate("ui.failedToUnlockHint")).message
    }
    finally {
      if (!disposed) submitting.value = false
    }
  }

  function retry(): void {
    unlockError.value = null
    void refreshHints()
    if (pendingFactId.value) start()
  }

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(props.competitionId, {
      competitionEventChanged: event => {
        if (affectsChallengeHints(event.kind)) void refreshHints()
      },
      gameplayFactStateChanged: fact => {
        if (fact.kind === 'HintUnlock' && fact.competitionChallengeId === props.competitionChallengeId)
          void refreshHints()
      },
      onReconnected: () => void refreshHints(),
    })
  })

  onUnmounted(() => {
    disposed = true
    requestGuard.invalidate()
    unwatch?.()
    stop()
  })

  const viewBindings = {
      ...toRefs(props),
      Lightbulb,
      LockKeyhole,
      readableHintContent,
      isLoggedIn,
      headingId,
      hints,
      refreshing,
      refreshError,
      unlockError,
      confirmingId,
      submitting,
      pendingFactId,
      busy,
      refreshHints,
      start,
      pollingError,
      timedOut,
      unlock,
      retry
    }
  const viewState = proxyRefs(viewBindings)

  function onClickConfirmingId(value: typeof viewState.confirmingId) {
    viewState.confirmingId = value
  }

  function onClickConfirmingId2(value: typeof viewState.confirmingId) {
    viewState.confirmingId = value
  }

  return { ...viewBindings, onClickConfirmingId, onClickConfirmingId2 }
}

export type ChallengeHintsViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useChallengeHints>>>
