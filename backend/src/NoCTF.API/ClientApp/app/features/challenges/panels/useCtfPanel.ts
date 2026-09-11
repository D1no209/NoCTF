import { markRaw, toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { isCtfPracticeOpen } from '../../../lib/competition-participation'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../api'
import FlagSubmitComponent from '../FlagSubmit.vue'
import RuntimeCardComponent from '../RuntimeCard.vue'

/** Owns state, effects and commands for CtfPanel. */
export function useCtfPanel(props: Readonly<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
  flagDockTarget?: string
  runtimeDockTarget?: string
}>,
emit: { (event: "submitted", ...args: []): void; (event: "remainingChanged", ...args: [remaining: number | null]): void }) {
  const practiceOpen = computed(() => isCtfPracticeOpen(props.competition))

  const actionsAvailable = computed(() => props.competition.status === 'Running' || practiceOpen.value)

  const runtimeCard = ref<{ refreshUntilStopped: () => Promise<void> } | null>(null)

  function handleEvaluation(result?: string | null): void {
    if (result === 'Correct' && !practiceOpen.value)
      void runtimeCard.value?.refreshUntilStopped()
  }

  const FlagSubmit = markRaw(FlagSubmitComponent)

  const RuntimeCard = markRaw(RuntimeCardComponent)

  function setRuntimeCardRef(element: Element | ComponentPublicInstance | null) { runtimeCard.value = element as typeof runtimeCard.value }

  return {
      ...toRefs(props),
      practiceOpen,
      actionsAvailable,
      emit,
      runtimeCard,
      handleEvaluation,
      FlagSubmit,
      RuntimeCard,
      setRuntimeCardRef
    }
}

export type CtfPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCtfPanel>>>
