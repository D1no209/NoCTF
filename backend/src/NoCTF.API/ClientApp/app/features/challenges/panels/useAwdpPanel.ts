import { markRaw, toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { ShieldCheck } from '@lucide/vue'
import { getAwdpParticipantStateEndpoint } from '../../../api'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse, NoCtfapiEndpointsGameplayFactsAwdpParticipantStateResponse } from '../../../api'
import FixSubmitComponent from '../FixSubmit.vue'
import FlagSubmitComponent from '../FlagSubmit.vue'
import RuntimeCardComponent from '../RuntimeCard.vue'

type Events = { submitted: [] }

/** Owns state, effects and commands for AwdpPanel. */
export function useAwdpPanel(props: Readonly<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>,
emit: { (event: "submitted", ...args: []): void }) {
  const state = ref<NoCtfapiEndpointsGameplayFactsAwdpParticipantStateResponse | null>(null)

  const loading = ref(true)

  const stateError = ref<string | null>(null)

  const attackRuntimeCard = ref<{ refreshUntilStopped: () => Promise<void> } | null>(null)

  const defenseTransitionInProgress = computed(() => {
    const factState = state.value?.defense?.state
    const runtimeState = state.value?.defense?.runtimeState
    return factState === 'Pending'
      || factState === 'Queued'
      || factState === 'Processing'
      || runtimeState === 'Queued'
      || runtimeState === 'Provisioning'
      || runtimeState === 'Stopping'
  })

  const defenseOutcome = computed(() => {
    const defense = state.value?.defense
    if (!defense?.gameplayFactId || !defense.result && !defense.failureCode) return null
    if (defense.result === 'Correct') return translate("ui.defenseSucceeded")
    if (defense.failureCode === 'AwdpExploitSucceeded') return translate("ui.defenseFailedExploitSucceeded")
    if (defense.failureCode === 'AwdpServiceAbnormal') return translate("ui.defenseFailedServiceAbnormal")
    if (defense.state === 'PlatformFailed') return translate("ui.defenseVerificationFailed")
    return translate("ui.defenseFailedServiceAbnormal")
  })

  async function refreshState(): Promise<boolean> {
    const { data, error } = await getAwdpParticipantStateEndpoint({
      path: {
        competitionId: props.competition.id!,
        competitionChallengeId: props.challenge.id!,
      },
    })
    loading.value = false
    if (error || !data) {
      stateError.value = parseApiError(error, translate("ui.failedToLoadTheAwdpChallengeState")).message
      return false
    }
    stateError.value = null
    state.value = data
    return !defenseTransitionInProgress.value
  }

  const { timedOut: statePollingTimedOut, start: startStatePolling, stop: stopStatePolling } = usePolling(
    refreshState,
    { interval: 1500, timeout: 300_000 },
  )

  async function refreshAndPoll(): Promise<void> {
    const complete = await refreshState()
    if (!complete) startStatePolling()
  }

  async function handleBreakEvaluation(result?: string | null): Promise<void> {
    await refreshAndPoll()
    if (result === 'Correct') await attackRuntimeCard.value?.refreshUntilStopped()
  }

  async function handleFixAccepted(): Promise<void> {
    emit('submitted')
    await refreshAndPoll()
  }

  let unwatch: (() => void) | undefined

  onMounted(() => {
    void refreshAndPoll()
    unwatch = watchCompetition(props.competition.id!, {
      gameplayFactStateChanged: () => void refreshAndPoll(),
    })
  })

  onUnmounted(() => {
    unwatch?.()
    stopStatePolling()
  })

  const FixSubmit = markRaw(FixSubmitComponent)

  const FlagSubmit = markRaw(FlagSubmitComponent)

  const RuntimeCard = markRaw(RuntimeCardComponent)

  function setAttackRuntimeCardRef(element: Element | ComponentPublicInstance | null) { attackRuntimeCard.value = element as typeof attackRuntimeCard.value }

  return {
      ...toRefs(props),
      ShieldCheck,
      emit,
      state,
      loading,
      stateError,
      attackRuntimeCard,
      defenseOutcome,
      statePollingTimedOut,
      refreshAndPoll,
      handleBreakEvaluation,
      handleFixAccepted,
      FixSubmit,
      FlagSubmit,
      RuntimeCard,
      setAttackRuntimeCardRef
    }
}

export type AwdpPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAwdpPanel>>>
