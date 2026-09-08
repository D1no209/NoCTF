import { markRaw, toRefs } from 'vue'

import { Check, Clipboard, FlaskConical, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { publicGatewayFailure } from '../../utils/public-gateway'
import { adminChallengeBankExtendTestRuntime, adminChallengeBankGetTestRuntime, adminChallengeBankResetTestRuntime, adminChallengeBankStartTestRuntime, adminChallengeBankStopTestRuntime } from '../../api'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeAcceptedResponse, NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse, NoCtfapiEndpointsAdministrationChallengeBankRuntimeTestFlagStateProtocol } from '../../api'
import type { ChallengeTestRuntimeLoadOutcome, ChallengeTestRuntimeMutationKind, PendingChallengeTestRuntimeMutation } from '../../utils/challenge-test-runtime-polling'
import RuntimeAccessUrlComponent from '../challenges/RuntimeAccessUrl.vue'

type TestRuntime = NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse

/** Owns state, effects and commands for ChallengeTestRuntimePanel. */
export function useChallengeTestRuntimePanel(props: Readonly<{
  challengeId: string
  definitionDirty: boolean
}>) {
  const runtime = ref<TestRuntime | null>(null)

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  const acting = ref(false)

  const pendingMutation = ref<PendingChallengeTestRuntimeMutation | null>(null)

  const copied = ref(false)

  const now = ref(Date.now())

  const extendMinutes = ref(30)

  let copiedTimer: ReturnType<typeof setTimeout> | undefined

  let clockTimer: ReturnType<typeof setInterval> | undefined

  async function load(): Promise<ChallengeTestRuntimeLoadOutcome> {
    try {
      const { data, error, response } = await adminChallengeBankGetTestRuntime({
        path: { challengeId: props.challengeId },
      })
      if (response?.status === 404) {
        runtime.value = null
        loadError.value = null
        return 'missing'
      }
      if (error || !data) {
        loadError.value = parseApiError(error, translate("ui.failedToLoadTheChallengeTestContainer")).message
        return 'failed'
      }
      runtime.value = data
      loadError.value = null
      return 'available'
    }
    catch (error) {
      loadError.value = parseApiError(error, translate("ui.failedToLoadTheChallengeTestContainer")).message
      return 'failed'
    }
  }

  function shouldContinuePolling(outcome: ChallengeTestRuntimeLoadOutcome): boolean {
    const decision = evaluateChallengeTestRuntimePolling(
      outcome,
      runtime.value,
      pendingMutation.value,
    )
    if (decision.mutationObserved)
      pendingMutation.value = null
    return decision.continuePolling
  }

  const { timedOut, start: startPolling } = usePolling(
    async () => {
      const outcome = await load()
      return !shouldContinuePolling(outcome)
    },
    { interval: 1500, timeout: 180_000 },
  )

  onMounted(async () => {
    const outcome = await load()
    loading.value = false
    if (shouldContinuePolling(outcome))
      startPolling()
    clockTimer = setInterval(() => { now.value = Date.now() }, 1000)
  })

  onUnmounted(() => {
    if (copiedTimer) clearTimeout(copiedTimer)
    if (clockTimer) clearInterval(clockTimer)
  })

  async function retryLoad(): Promise<void> {
    loading.value = true
    const outcome = await load()
    loading.value = false
    if (shouldContinuePolling(outcome))
      startPolling()
  }

  async function act(
    kind: ChallengeTestRuntimeMutationKind,
    action: () => Promise<{
      data?: NoCtfapiEndpointsAdministrationChallengeBankChallengeTestRuntimeAcceptedResponse
      error?: unknown
    }>,
    failureMessage: string,
  ): Promise<void> {
    if (acting.value) return
    acting.value = true
    try {
      const previousRuntimeInstanceId = runtime.value?.id
      const previousExpiresAt = runtime.value?.expiresAt
      const { data, error } = await action()
      if (error) throw error
      pendingMutation.value = {
        kind,
        runtimeInstanceId: data?.runtimeInstanceId,
        previousRuntimeInstanceId,
        previousExpiresAt,
      }
      toast.success(translate("ui.theOperationWasAcceptedTestContainerStatusIsUpdating"))
      const outcome = await load()
      if (shouldContinuePolling(outcome))
        startPolling()
    }
    catch (error) {
      toast.error(parseApiError(error, failureMessage).message)
    }
    finally {
      acting.value = false
    }
  }

  const path = computed(() => ({ challengeId: props.challengeId }))

  const start = () => act(
    'start',
    () => adminChallengeBankStartTestRuntime({ path: path.value }),
    translate("ui.failedToStartTheChallengeTestContainer"),
  )

  const stop = () => act(
    'stop',
    () => adminChallengeBankStopTestRuntime({ path: path.value }),
    translate("ui.failedToStopTheChallengeTestContainer"),
  )

  const reset = () => act(
    'reset',
    () => adminChallengeBankResetTestRuntime({ path: path.value }),
    translate("ui.failedToResetTheChallengeTestContainer"),
  )

  const extend = () => act(
    'extend',
    () => adminChallengeBankExtendTestRuntime({
      path: path.value,
      body: { seconds: Math.max(60, Math.round(extendMinutes.value * 60)) },
    }),
    translate("ui.failedToExtendTheChallengeTestContainer"),
  )

  async function copyTestFlag(): Promise<void> {
    if (!runtime.value?.testFlag) return
    try {
      await navigator.clipboard.writeText(runtime.value.testFlag)
      copied.value = true
      if (copiedTimer) clearTimeout(copiedTimer)
      copiedTimer = setTimeout(() => { copied.value = false }, 1800)
    }
    catch {
      toast.error(translate("ui.couldNotCopyTheTestFlagSelectAndCopyIt"))
    }
  }

  const active = computed(() => runtime.value?.state === 'Queued'
    || runtime.value?.state === 'Provisioning'
    || runtime.value?.state === 'Running')

  const canStart = computed(() => !runtime.value
    || runtime.value.state === 'Stopped'
    || runtime.value.state === 'Failed')

  const waitingForAcceptedRuntime = computed(() => {
    const pending = pendingMutation.value
    if (!pending || pending.kind !== 'start' && pending.kind !== 'reset') return false
    if (!runtime.value) return true
    if (pending.runtimeInstanceId)
      return runtime.value.id !== pending.runtimeInstanceId
    return pending.kind === 'reset'
      && runtime.value.id === pending.previousRuntimeInstanceId
  })

  const busy = computed(() => acting.value || waitingForAcceptedRuntime.value)

  const ttl = computed(() => {
    if (!runtime.value?.expiresAt) return null
    const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
    return remaining > 0 ? formatDuration(remaining) : translate("ui.expired")
  })

  const canExtend = computed(() => {
    if (runtime.value?.state !== 'Running' || !runtime.value.expiresAt) return false
    const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
    return remaining > 0 && remaining < 10 * 60_000
  })

  const stateVariant = computed(() => {
    if (runtime.value?.state === 'Running') return 'default' as const
    if (runtime.value?.state === 'Failed') return 'destructive' as const
    if (runtime.value?.state === 'Stopped') return 'secondary' as const
    return 'outline' as const
  })

  const flagVariant = computed(() => runtime.value?.flagState === 'Failed'
    ? 'destructive' as const
    : runtime.value?.flagState === 'Succeeded'
      ? 'default' as const
      : 'secondary' as const)

  function flagStateLabel(state?: NoCtfapiEndpointsAdministrationChallengeBankRuntimeTestFlagStateProtocol): string {
    switch (state) {
      case 'Pending': return translate("ui.injecting")
      case 'Succeeded': return translate("ui.injected")
      case 'Failed': return translate("ui.injectionFailed")
      case 'Canceled': return translate("ui.canceled")
      default: return translate("ui.notRequired")
    }
  }

  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  return {
      ...toRefs(props),
      Check,
      Clipboard,
      FlaskConical,
      RefreshCw,
      publicGatewayFailure,
      runtime,
      loading,
      loadError,
      copied,
      extendMinutes,
      timedOut,
      retryLoad,
      start,
      stop,
      reset,
      extend,
      copyTestFlag,
      active,
      canStart,
      busy,
      ttl,
      canExtend,
      stateVariant,
      flagVariant,
      flagStateLabel,
      RuntimeAccessUrl
    }
}

export type ChallengeTestRuntimePanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useChallengeTestRuntimePanel>>>
