import { computed, markRaw, ref, toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { isCtfPracticeOpen } from '../../../lib/competition-participation'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../api'
import FlagSubmitComponent from '../FlagSubmit.vue'
import RuntimeCardComponent from '../RuntimeCard.vue'

export function useCtfPanel(props: Readonly<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
  flagDockTarget?: string
  runtimeDockTarget?: string
}>, emit: { (event: 'submitted'): void; (event: 'remainingChanged', remaining: number | null): void }) {
  const practiceOpen = computed(() => isCtfPracticeOpen(props.competition))
  const actionsAvailable = computed(() => props.competition.status === 'Running' || practiceOpen.value)
  const runtimeCard = ref<{ refreshUntilStopped: () => Promise<void> } | null>(null)
  function handleEvaluation(result?: string | null) {
    if (result === 'Correct' && !practiceOpen.value) void runtimeCard.value?.refreshUntilStopped()
  }
  function setRuntimeCardRef(element: Element | ComponentPublicInstance | null) { runtimeCard.value = element as typeof runtimeCard.value }
  return { ...toRefs(props), practiceOpen, actionsAvailable, emit, handleEvaluation, setRuntimeCardRef,
    FlagSubmit: markRaw(FlagSubmitComponent), RuntimeCard: markRaw(RuntimeCardComponent) }
}
export type CtfPanelViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useCtfPanel>>
