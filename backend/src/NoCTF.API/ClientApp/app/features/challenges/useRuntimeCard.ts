import { markRaw, toRefs } from 'vue'

import { toast } from 'vue-sonner'
import { extendRuntimeEndpoint, getRuntimeEndpoint, resetRuntimeEndpoint, startRuntimeEndpoint, stopRuntimeEndpoint } from '../../api'
import type { NoCtfapiEndpointsRuntimeRuntimeResponse } from '../../api'
import { publicGatewayFailure, publicGatewayState } from '../../utils/public-gateway'
import { classifyPlayerRuntimeLookup, normalizePlayerRuntime, shouldPollPlayerRuntime, type PlayerRuntimeLookupOutcome } from '../../utils/player-runtime'
import RuntimeAccessUrlComponent from './RuntimeAccessUrl.vue'

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
    { interval: 2000, timeout: 120_000 },
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

  async function act(action: () => Promise<{ error?: unknown }>, failMessage: string) {
    if (acting.value) return
    acting.value = true
    try {
    const { error } = await action()
    if (error) {
      toast.error(parseApiError(error, failMessage).message)
      return
    }
    commandAttempt.completed()
    toast.success(translate("ui.theOperationHasBeenAcceptedAndTheEnvironmentStatusIs"))
    startPolling()
    } catch (e) { toast.error(parseApiError(e, failMessage).message) }
    finally { acting.value = false }
  }

  const path = computed(() => ({
    competitionId: props.competitionId,
    competitionChallengeId: props.competitionChallengeId,
  }))

  const start = () => act(() => startRuntimeEndpoint({ path: path.value, headers: commandAttempt.headers({ ...path.value, action: 'start' }) }), translate("ui.failedToStartEnvironment"))

  const stop = () => act(() => stopRuntimeEndpoint({ path: path.value, headers: commandAttempt.headers({ ...path.value, action: 'stop' }) }), translate("ui.stopEnvironmentFailed"))

  const reset = () => act(() => resetRuntimeEndpoint({ path: path.value, headers: commandAttempt.headers({ ...path.value, action: 'reset' }) }), translate("ui.failedToResetEnvironment"))

  const extend = () =>
    act(
      () =>
        extendRuntimeEndpoint({
          headers: commandAttempt.headers({ ...path.value, action: 'extend', minutes: extendMinutes.value }),
          path: path.value,
          body: { seconds: Math.max(60, Math.round(extendMinutes.value * 60)) },
        }),
      translate("ui.renewalFailed"),
    )

  let timer: ReturnType<typeof setInterval> | undefined

  let publicTimer: ReturnType<typeof setInterval> | undefined

  let publicRefreshing = false

  onMounted(() => {
    timer = setInterval(() => {
      now.value = Date.now()
    }, 1000)
  })

  watch(() => runtime.value?.access?.route === 'Gateway' && runtime.value.state === 'Running', (needsPublicRefresh) => {
    if (publicTimer) clearInterval(publicTimer)
    publicTimer = undefined
    if (!needsPublicRefresh) return
    publicTimer = setInterval(async () => {
      if (publicRefreshing || acting.value || polling.value || runtime.value?.access?.route !== 'Gateway' || runtime.value.state !== 'Running') return
      publicRefreshing = true
      try { await load() }
      finally { publicRefreshing = false }
    }, 5000)
  })

  onUnmounted(() => {
    if (timer) clearInterval(timer)
    if (publicTimer) clearInterval(publicTimer)
  })

  const ttl = computed(() => {
    if (!runtime.value?.expiresAt) return null
    const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
    return remaining > 0 ? formatDuration(remaining) : translate("ui.expired")
  })

  const isRunning = computed(() => runtime.value?.state === 'Running')

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
      publicGatewayFailure,
      publicGatewayState,
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
