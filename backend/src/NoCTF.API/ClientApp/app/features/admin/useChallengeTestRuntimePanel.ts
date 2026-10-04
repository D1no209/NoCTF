
import { ResponseMetadata, RequestPolicyOption } from '../../lib/api'

import { api } from '../../lib/api'
import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { markRaw, toRefs } from 'vue'

import { Check, Clipboard, FlaskConical, RefreshCw } from '@lucide/vue'
import { toast } from '../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationChallengeBankChallengeTestRuntimeAcceptedResponse, NoCTFAPIEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse, NoCTFAPIEndpointsAdministrationChallengeBankRuntimeTestFlagStateProtocol } from '../../api/models'
import type { ChallengeTestRuntimeLoadOutcome, ChallengeTestRuntimeMutationKind, PendingChallengeTestRuntimeMutation } from '../../utils/challenge-test-runtime-polling'
import RuntimeFlagsPanelComponent from './RuntimeFlagsPanel.vue'
import RuntimeAccessUrlComponent from '../challenges/RuntimeAccessUrl.vue'
import { createRuntimeExtensionRequest, isRuntimeExtensionTooEarly, parseRuntimeExtensionMinutes } from '../../lib/runtime-extension'

type TestRuntime = NoCTFAPIEndpointsAdministrationChallengeBankChallengeTestRuntimeResponse

/** Owns state, effects and commands for ChallengeTestRuntimePanel. */
export function useChallengeTestRuntimePanel(props: Readonly<{
  challengeId: string
  definitionDirty: boolean
}>) {
  const runtime = ref<TestRuntime | null>(null)

  const loading = ref(true)

  const loadError = ref<UiMessage | null>(null)

  const acting = ref(false)

  const pendingMutation = ref<PendingChallengeTestRuntimeMutation | null>(null)

  const copied = ref(false)

  const now = ref(Date.now())

  const extendMinutes = ref<number | string>(30)

  let copiedTimer: ReturnType<typeof setTimeout> | undefined

  let clockTimer: ReturnType<typeof setInterval> | undefined

  async function load(): Promise<ChallengeTestRuntimeLoadOutcome> {
    try {
      let error: unknown;
      const response = new ResponseMetadata();
      const data = await api.api.v1.admin.challenges.byChallengeId(props.challengeId).testRuntimes.current.get({ options: [new RequestPolicyOption({ response: response })] }).catch(cause => { error = cause; return undefined });
      if (response?.status === 404) {
        runtime.value = null
        loadError.value = null
        return 'missing'
      }
      if (error || !data) {
        loadError.value = parseApiError(error, describeMessage("runtime.challengeTest.error.loadChallengeTestFailed")).displayMessage
        return 'failed'
      }
      runtime.value = data
      loadError.value = null
      return 'available'
    }
    catch (error) {
      loadError.value = parseApiError(error, describeMessage("runtime.challengeTest.error.loadChallengeTestFailed")).displayMessage
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
    action: () => Promise<NoCTFAPIEndpointsAdministrationChallengeBankChallengeTestRuntimeAcceptedResponse | undefined>,
    failureMessage: string,
  ): Promise<void> {
    if (acting.value) return
    acting.value = true
    try {
      const previousRuntimeInstanceId = runtime.value?.id
      const previousExpiresAt = runtime.value?.expiresAt
      const data = await action()
      pendingMutation.value = {
        kind,
        runtimeInstanceId: data?.runtimeInstanceId,
        previousRuntimeInstanceId,
        previousExpiresAt,
      }
      toast.success(describeMessage("runtime.challengeTest.description.wasAcceptedTestContainer"))
      const outcome = await load()
      if (shouldContinuePolling(outcome))
        startPolling()
    }
    catch (error) {
      toast.error(parseApiError(error, failureMessage).displayMessage)
    }
    finally {
      acting.value = false
    }
  }

  const path = computed(() => ({ challengeId: props.challengeId }))

  const start = () => act(
    'start',
    () => api.api.v1.admin.challenges.byChallengeId(path.value.challengeId).testRuntimes.post({ replacesRuntimeId: null }),
    translate("runtime.challengeTest.error.startChallengeTestFailed"),
  )

  const stop = () => act(
    'stop',
    () => api.api.v1.admin.challenges.byChallengeId(path.value.challengeId).testRuntimes.byRuntimeInstanceId(runtime.value!.id!).delete(),
    translate("runtime.challengeTest.error.stopChallengeTestFailed"),
  )

  const reset = () => act(
    'reset',
    () => api.api.v1.admin.challenges.byChallengeId(path.value.challengeId).testRuntimes.post({ replacesRuntimeId: runtime.value!.id! }),
    translate("runtime.challengeTest.error.resetChallengeTestFailed"),
  )

  const extend = () => {
    const current = runtime.value
    const extension = createRuntimeExtensionRequest(
      current?.expiresAt, Date.now(), extendMinutes.value, 1440)
    if (current?.state !== 'Running' || !current.id || extension === null) return
    return act(
      'extend',
      () => api.api.v1.admin.challenges.byChallengeId(path.value.challengeId).testRuntimes.byRuntimeInstanceId(current.id!).patch({ expiresAt: extension.expiresAt }),
      translate("runtime.challengeTest.error.extendChallengeTestFailed"),
    )
  }

  async function copyTestFlag(): Promise<void> {
    if (!runtime.value?.testFlag) return
    try {
      await navigator.clipboard.writeText(runtime.value.testFlag)
      copied.value = true
      if (copiedTimer) clearTimeout(copiedTimer)
      copiedTimer = setTimeout(() => { copied.value = false }, 1800)
    }
    catch {
      toast.error(describeMessage("runtime.challengeTest.description.couldCopyTestFlag"))
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
    return remaining > 0 ? formatDuration(remaining) : translate("runtime.label.expired")
  })

  const canExtend = computed(() => {
    if (runtime.value?.state !== 'Running' || !runtime.value.expiresAt) return false
    const remaining = new Date(runtime.value.expiresAt).getTime() - now.value
    return remaining > 0
  })
  const renewalTooEarly = computed(() => runtime.value?.state === 'Running'
    && isRuntimeExtensionTooEarly(runtime.value.expiresAt, now.value))
  const extendMinutesInvalid = computed(() =>
    parseRuntimeExtensionMinutes(extendMinutes.value, 1440) === null)
  const validExtension = computed(() => canExtend.value && !busy.value
    && createRuntimeExtensionRequest(
      runtime.value?.expiresAt, now.value, extendMinutes.value, 1440) !== null)

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

  function flagStateLabel(state?: NoCTFAPIEndpointsAdministrationChallengeBankRuntimeTestFlagStateProtocol | null): string {
    switch (state) {
      case 'Pending': return translate("runtime.label.injecting")
      case 'Succeeded': return translate("common.label.injected")
      case 'Failed': return translate("runtime.error.injectionFailed")
      case 'Canceled': return translate("runtime.label.canceled")
      default: return translate("runtime.validation.required")
    }
  }

  const RuntimeFlagsPanel = markRaw(RuntimeFlagsPanelComponent)
  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  return {
      ...toRefs(props),
      Check,
      Clipboard,
      FlaskConical,
      RefreshCw,
      runtime,
      loading,
      loadError,
      copied,
      extendMinutes,
      extendMinutesInvalid,
      renewalTooEarly,
      validExtension,
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
      RuntimeFlagsPanel,
      RuntimeAccessUrl
    }
}

export type ChallengeTestRuntimePanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useChallengeTestRuntimePanel>>>
