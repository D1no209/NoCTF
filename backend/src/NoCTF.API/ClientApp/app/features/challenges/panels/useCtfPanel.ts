
import { api } from '../../../lib/api'
import { markRaw, toRefs } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { isCtfPracticeOpen } from '../../../lib/competition-participation'

import type { NoCTFAPIEndpointsChallengesChallengeResponse, NoCTFAPIEndpointsCompetitionsCompetitionResponse, NoCTFAPIEndpointsGameplayFactsPatchVerificationStateResponse } from '../../../api/models'
import FlagSubmitComponent from '../FlagSubmit.vue'
import FixSubmitComponent from '../FixSubmit.vue'
import RuntimeCardComponent from '../RuntimeCard.vue'

/** Owns state, effects and commands for CtfPanel. */
export function useCtfPanel(props: Readonly<{
  competition: NoCTFAPIEndpointsCompetitionsCompetitionResponse
  challenge: NoCTFAPIEndpointsChallengesChallengeResponse
  flagDockTarget?: string | null
  runtimeDockTarget?: string | null
}>,
emit: { (event: "submitted", ...args: []): void; (event: "remainingChanged", ...args: [remaining: number | null]): void }) {
  const practiceOpen = computed(() => isCtfPracticeOpen(props.competition))

  const isPatchVerification = computed(() => props.challenge.interactionKind === 'PatchVerification')

  const actionsAvailable = computed(() => isPatchVerification.value
    ? props.challenge.patchVerificationAvailable === true
    : props.competition.status === 'Running' || practiceOpen.value)

  const patchVerification = ref<NoCTFAPIEndpointsGameplayFactsPatchVerificationStateResponse>({
    patchVerificationAvailable: props.challenge.patchVerificationAvailable,
    maximumAttempts: props.challenge.maximumPatchAttempts ?? undefined,
    acceptedAttempts: props.challenge.acceptedPatchAttempts ?? undefined,
    remainingAttempts: props.challenge.remainingPatchAttempts ?? undefined,
    verificationState: props.challenge.patchVerificationState,
    verificationResult: props.challenge.patchVerificationResult,
    verificationFailureCode: props.challenge.patchVerificationFailureCode,
    runtimeInstanceId: props.challenge.patchVerificationRuntimeInstanceId,
    runtimeState: props.challenge.patchVerificationRuntimeState,
  })

  async function refreshPatchVerification(): Promise<boolean> {
    if (!isPatchVerification.value || !props.competition.id || !props.challenge.id) return true
    let error: unknown;
    const data = await api.api.v1.competitions.byCompetitionId(props.competition.id).challenges.byCompetitionChallengeId(props.challenge.id).patchVerification.get().catch(cause => { error = cause; return undefined });
    if (error || !data) throw parseApiError(error)
    patchVerification.value = data
    emit('remainingChanged', data.remainingAttempts ?? null)
    const factActive = data.verificationState === 'Pending'
      || data.verificationState === 'Queued'
      || data.verificationState === 'Processing'
    const runtimeActive = data.runtimeState === 'Queued'
      || data.runtimeState === 'Provisioning'
      || data.runtimeState === 'Stopping'
    return !factActive && !runtimeActive
  }

  const patchPolling = usePolling(refreshPatchVerification, {
    interval: 800,
    maxInterval: 4_000,
    timeout: 360_000,
  })

  const patchOutcome = computed(() => {
    if (patchVerification.value.verificationState === 'PlatformFailed')
      return { message: translate('common.error.patchVerificationPlatformFailed'), variant: 'destructive' as const }
    if (patchVerification.value.verificationResult === 'Correct')
      return { message: translate('challenges.label.patchVerificationSucceeded'), variant: 'default' as const }
    if (patchVerification.value.verificationResult !== 'Wrong') return null
    const key = patchVerification.value.verificationFailureCode === 'PatchStillExploitable'
      ? 'common.label.patchStillExploitable'
      : patchVerification.value.verificationFailureCode === 'PatchServiceAbnormal'
        ? 'common.label.patchServiceAbnormal'
        : 'common.error.patchExecutionFailed'
    return { message: translate(key), variant: 'destructive' as const }
  })

  async function handlePatchChanged(): Promise<void> {
    emit('submitted')
    try {
      const done = await refreshPatchVerification()
      if (!done) patchPolling.start()
    }
    catch {
      patchPolling.start()
    }
  }

  const runtimeCard = ref<{ refreshUntilStopped: () => Promise<void> } | null>(null)

  function handleEvaluation(result?: string | null): void {
    if (result === 'Correct' && !practiceOpen.value)
      void runtimeCard.value?.refreshUntilStopped()
  }

  const FlagSubmit = markRaw(FlagSubmitComponent)

  const FixSubmit = markRaw(FixSubmitComponent)

  const RuntimeCard = markRaw(RuntimeCardComponent)

  function setRuntimeCardRef(element: Element | ComponentPublicInstance | null) { runtimeCard.value = element as typeof runtimeCard.value }

  return {
      ...toRefs(props),
      practiceOpen,
      isPatchVerification,
      actionsAvailable,
      patchVerification,
      patchOutcome,
      handlePatchChanged,
      emit,
      runtimeCard,
      handleEvaluation,
      FlagSubmit,
      FixSubmit,
      RuntimeCard,
      setRuntimeCardRef
    }
}

export type CtfPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCtfPanel>>>
