import { markRaw, toRefs } from 'vue'

import { toast } from 'vue-sonner'
import { createRuntime, extendRuntimeEndpoint, getRuntimeEndpoint, stopRuntimeEndpoint } from '../../api'
import type { NoCtfapiEndpointsRuntimeRuntimeResponse } from '../../api'
import { runnerFailureLabel } from '../shared/runner-capacity'
import { classifyPlayerRuntimeLookup, normalizePlayerRuntime, shouldPollPlayerRuntime, type PlayerRuntimeLookupOutcome } from '../../utils/player-runtime'
import { RUNTIME_STOP_POLL_DELAYS_MS, RUNTIME_STOP_POLL_MAX_INTERVAL_MS, RUNTIME_STOP_POLL_TIMEOUT_MS } from '../../lib/runtime-stop-polling'
import RuntimeAccessUrlComponent from './RuntimeAccessUrl.vue'
import { useHumanVerification } from '~/features/security/useHumanVerification'

type Runtime = NoCtfapiEndpointsRuntimeRuntimeResponse

/** Owns state, effects and commands for RuntimeCard. */
export function useRuntimeCard(props: Readonly<Omit<{
    competitionId: string
    competitionChallengeId: string
    /** full = CTF 全操作;reset-only = AWD 仅重置;readonly = 只显示最终状态 */
    controls?: 'full' | 'reset-only' | 'readonly'
    dockTarget?: string
  }, "controls" | "dockTarget"> & Required<Pick<{
    competitionId: string
    competitionChallengeId: string
    /** full = CTF 全操作;reset-only = AWD 仅重置;readonly = 只显示最终状态 */
    controls?: 'full' | 'reset-only' | 'readonly'
    dockTarget?: string
  }, "controls" | "dockTarget">>>) {
  const runtime = ref<Runtime | null>(null)
  const { request: requestHumanVerification } = useHumanVerification()

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  const acting = ref(false)

  const commandAttempt = createCommandAttempt()

  const extendMinutes = ref(30)

  const now = ref(Date.now())

  const forceUntilStopped = ref(false)

  async function load(): Promise<PlayerRuntimeLookupOutcome> {
    const { data, error, response } = await getRuntimeEndpoint({
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
    })
    const outcome = classifyPlayerRuntimeLookup(response?.status, Boolean(error), Boolean(data))
    if (outcome === 'missing') {
      runtime.value = null
      loadError.value = null
      return outcome
    }
    if (outcome === 'failed') {
      loadError.value = parseApiError(error, translate("ui.failedToLoadTheEnvironmentStatus")).message
      return outcome
    }

    runtime.value = normalizePlayerRuntime(data ?? null)
    loadError.value = null
    return outcome
  }

  onMounted(async () => {
    const outcome = await load()
    loading.value = false
    if (outcome === 'available' && shouldPollPlayerRuntime(runtime.value, now.value)) startPolling()
  })

  const { polling, timedOut, start: startPolling } = usePolling(
    async () => {
      const outcome = await load()
      if (forceUntilStopped.value) {
        if (outcome === 'failed' || runtime.value?.state !== 'Running') {
          forceUntilStopped.value = false
          return true
        }
        return false
      }
      return outcome === 'failed' || !shouldPollPlayerRuntime(runtime.value, now.value)
    },
    {
      interval: 2_000,
      maxInterval: RUNTIME_STOP_POLL_MAX_INTERVAL_MS,
      timeout: RUNTIME_STOP_POLL_TIMEOUT_MS,
      delays: RUNTIME_STOP_POLL_DELAYS_MS,
    },
  )

  async function refreshUntilStopped(): Promise<void> {
    forceUntilStopped.value = true
    const outcome = await load()
    if (outcome === 'failed' || runtime.value?.state !== 'Running') {
      forceUntilStopped.value = false
      return
    }
    startPolling()
  }

  watch(
    () => shouldPollPlayerRuntime(runtime.value, now.value),
    (needsPolling) => {
      if (!loadError.value && needsPolling && runtime.value?.state === 'Running' && !polling.value)
        startPolling()
    },
  )

  async function retryLoad(): Promise<void> {
    loading.value = true
    const outcome = await load()
    loading.value = false
    if (outcome === 'available' && shouldPollPlayerRuntime(runtime.value, now.value))
      startPolling()
  }

  async function act(
    action: (humanVerificationHeaders: Record<string, string>) => Promise<{ error?: unknown }>,
    failMessage: string,
    onAccepted?: () => void,
  ) {
    if (acting.value) return
    acting.value = true
    try {
    const verificationHeaders = await requestHumanVerification('runtime')
    if (verificationHeaders === null) return
    const { error } = await action(verificationHeaders)
    if (error) {
      toast.error(parseApiError(error, failMessage).message)
      return
    }
    commandAttempt.completed()
    onAccepted?.()
    toast.success(translate("ui.theOperationHasBeenAcceptedAndTheEnvironmentStatusIs"))
    startPolling()
    } catch (e) { toast.error(parseApiError(e, failMessage).message) }
    finally { acting.value = false }
  }

  const path = computed(() => ({
    competitionId: props.competitionId,
    competitionChallengeId: props.competitionChallengeId,
  }))

  const start = () => act(verificationHeaders => createRuntime({
    path: path.value,
    body: { replacesRuntimeId: null },
    headers: { ...commandAttempt.headers({ ...path.value, action: 'start' }), ...verificationHeaders },
  }), translate("ui.failedToStartEnvironment"))

  const stop = () => {
    const current = runtime.value
    if (!current?.id) return
    return act(verificationHeaders => stopRuntimeEndpoint({
      path: { ...path.value, runtimeInstanceId: current.id! },
      headers: { ...commandAttempt.headers({ ...path.value, action: 'stop' }), ...verificationHeaders },
    }), translate("ui.stopEnvironmentFailed"), () => {
      if (runtime.value?.id === current.id)
        runtime.value = { ...runtime.value, state: 'Stopping' }
    })
  }

  const reset = () => runtime.value && act(verificationHeaders => createRuntime({
    path: path.value,
    body: { replacesRuntimeId: runtime.value!.id! },
    headers: { ...commandAttempt.headers({ ...path.value, action: 'reset' }), ...verificationHeaders },
  }), translate("ui.failedToResetEnvironment"))

  const extend = () =>
    act(
      verificationHeaders =>
        extendRuntimeEndpoint({
          headers: { ...commandAttempt.headers({ ...path.value, action: 'extend', minutes: extendMinutes.value }), ...verificationHeaders },
          path: { ...path.value, runtimeInstanceId: runtime.value!.id! },
          body: {
            expiresAt: new Date(
              new Date(runtime.value!.expiresAt!).getTime()
              + Math.max(60, Math.round(extendMinutes.value * 60)) * 1000,
            ).toISOString(),
          },
        }),
      translate("ui.renewalFailed"),
    )

  let timer: ReturnType<typeof setInterval> | undefined

  onMounted(() => {
    timer = setInterval(() => {
      now.value = Date.now()
    }, 1000)
  })

  onUnmounted(() => {
    if (timer) clearInterval(timer)
  })

  const ttl = computed(() => {
    if (!runtime.value?.expiresAt) return null
    const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
    return remaining > 0 ? formatDuration(remaining) : translate("ui.expired")
  })

  const isRunning = computed(() => runtime.value?.state === 'Running')
  const canStop = computed(() => runtime.value && ['Running', 'Queued', 'Provisioning'].includes(runtime.value.state ?? ''))
  const stopDisabled = computed(() => acting.value || runtime.value?.state === 'Stopping')

  const busy = computed(() => acting.value || polling.value)

  const stateVariant = computed(() => {
    switch (runtime.value?.state) {
      case 'Running':
        return 'default' as const
      case 'Failed':
        return 'destructive' as const
      case 'Stopped':
        return 'secondary' as const
      default:
        return 'outline' as const
    }
  })

  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  return {
      ...toRefs(props),
      runnerFailureLabel,
      canStop,
      stopDisabled,
      runtime,
      loading,
      loadError,
      extendMinutes,
      polling,
      timedOut,
      refreshUntilStopped,
      retryLoad,
      start,
      stop,
      reset,
      extend,
      ttl,
      isRunning,
      busy,
      stateVariant,
      RuntimeAccessUrl
    }
}

export type RuntimeCardViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useRuntimeCard>>>
