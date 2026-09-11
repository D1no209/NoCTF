import { toRefs } from 'vue'

import { toast } from 'vue-sonner'
import { requestAwdpDefenseTargetEndpoint, uploadPatchEndpoint } from '../../api'
import type { NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse, NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol, NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol } from '../../api'
import { useHumanVerification } from '~/features/security/useHumanVerification'

/** Owns state, effects and commands for FixSubmit. */
export function useFixSubmit(props: Readonly<{
  competitionId: string
  competitionChallengeId: string
  defense?: NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse
}>,
emit: { (event: "changed", ...args: []): void; (event: "accepted", ...args: []): void }) {
  const { request: requestHumanVerification } = useHumanVerification()
  const file = ref<File | null>(null)

  const pendingAction = ref<'request' | 'upload' | null>(null)

  const requestFailureLabels: Record<NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol, string> = {
    DefenseNotAvailable: "ui.thisCompetitionTeamOrChallengeDoesNotCurrentlyAllowA",
    ActiveDefenseTargetExists: "ui.aOneShotDefenseVerificationEnvironmentAlreadyExistsCompleteIt",
    DefenseAlreadySucceeded: "ui.fixHasAlreadySucceededFurtherFixAttemptsAreNotAccepted",
    BreakRequired: "ui.thisChallengeRequiresAValidBreakBeforeDefenseVerificationCan",
    FixAttemptsExhausted: "ui.thisChallengeSFixAttemptsAreExhausted",
    InvalidRuntimeConfiguration: "ui.theOneShotDefenseVerificationEnvironmentIsMisconfiguredContactCompetition",
    DefenseTargetConcurrency: "ui.theDefenseVerificationStateChangedRefreshAndTryAgain",
  }

  const uploadFailureLabels: Record<NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol, string> = {
    ArchiveStreamNotSeekable: "ui.theFixArchiveCannotBeValidatedSelectTheFileAgain",
    ArchiveInvalid: "ui.theFixArchiveIsInvalidUploadAValidTarGz",
    DefenseTargetNotReady: "ui.thisOneShotDefenseVerificationEnvironmentHasExpiredOrNo",
    DefenseAlreadySucceeded: "ui.fixHasAlreadySucceededFurtherFixAttemptsAreNotAccepted",
    FixAttemptsExhausted: "ui.thisChallengeSFixAttemptsAreExhausted",
    DefenseTargetConsumed: "ui.thisOneShotDefenseVerificationEnvironmentAlreadyHasAFix",
  }

  const runtimeActive = computed(() => {
    const state = props.defense?.runtimeState
    return state === 'Queued' || state === 'Provisioning' || state === 'Running' || state === 'Stopping'
  })

  const targetCreating = computed(() => {
    const state = props.defense?.runtimeState
    return state === 'Queued' || state === 'Provisioning'
  })

  const canUpload = computed(() =>
    (props.defense?.runtimeState === 'Queued'
      || props.defense?.runtimeState === 'Provisioning'
      || props.defense?.runtimeState === 'Running')
    && !props.defense.gameplayFactId,
  )

  const validating = computed(() =>
    props.defense?.state === 'Pending'
    || props.defense?.state === 'Queued'
    || props.defense?.state === 'Processing'
  )

  const recycling = computed(() => props.defense?.runtimeState === 'Stopping')

  const patchWaitingForTarget = computed(() =>
    !!props.defense?.gameplayFactId && targetCreating.value,
  )

  const targetFailed = computed(() => props.defense?.runtimeState === 'Failed')

  const completedAndRecycled = computed(() =>
    props.defense?.runtimeState === 'Stopped' && !!props.defense.gameplayFactId,
  )

  const canRequest = computed(() => !runtimeActive.value)

  function protocolCode<T extends string>(error: unknown): T | null {
    if (!error || typeof error !== 'object' || !('code' in error)) return null
    const code = (error as { code?: unknown }).code
    return typeof code === 'string' ? code as T : null
  }

  function onFileChange(event: Event): void {
    const target = event.target as HTMLInputElement
    file.value = target.files?.[0] ?? null
  }

  const targetCommandAttempt = createCommandAttempt()

  async function requestTarget(): Promise<void> {
    if (!canRequest.value || pendingAction.value) return
    pendingAction.value = 'request'
    try {
      const verificationHeaders = await requestHumanVerification('evaluation')
      if (verificationHeaders === null) return
      const { data, error } = await requestAwdpDefenseTargetEndpoint({
        headers: { ...targetCommandAttempt.headers({ competitionId: props.competitionId, challengeId: props.competitionChallengeId }), ...verificationHeaders },
        signal: AbortSignal.timeout(30_000),
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
        },
      })
      if (error || !data?.runtimeInstanceId) {
        const code = protocolCode<NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol>(error)
        toast.error(code ? translate(requestFailureLabels[code]) : parseApiError(error, translate("ui.failedToRequestTheDefenseVerificationEnvironment")).message)
        return
      }
      targetCommandAttempt.completed()
      toast.success(translate("ui.aOneShotDefenseVerificationEnvironmentWasRequestedAClean"))
      emit('changed')
    }
    finally {
      pendingAction.value = null
    }
  }

  const patchCommandAttempt = createCommandAttempt()

  async function uploadFix(): Promise<void> {
    const runtimeInstanceId = props.defense?.runtimeInstanceId
    if (!file.value || !canUpload.value || !runtimeInstanceId || pendingAction.value) return
    pendingAction.value = 'upload'
    try {
      const verificationHeaders = await requestHumanVerification('evaluation')
      if (verificationHeaders === null) return
      const { data, error } = await uploadPatchEndpoint({
        signal: AbortSignal.timeout(360_000),
        headers: { ...patchCommandAttempt.headers({ runtimeInstanceId, name: file.value.name, size: file.value.size, modified: file.value.lastModified }), ...verificationHeaders },
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
          runtimeInstanceId,
        },
        body: { file: file.value },
      })
      if (error || !data?.gameplayFactId) {
        const code = protocolCode<NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol>(error)
        const message = code
          ? uploadFailureLabels[code]
          : parseApiError(error, translate("ui.fixUploadFailed")).message
        toast.error(translate(message))
        return
      }
      patchCommandAttempt.completed()
      file.value = null
      toast.success(translate("ui.theFixIsLockedOneShotVerificationWillStartAutomatically"))
      emit('accepted')
      emit('changed')
    }
    finally {
      pendingAction.value = null
    }
  }

  return {
      ...toRefs(props),
      file,
      pendingAction,
      targetCreating,
      canUpload,
      validating,
      recycling,
      patchWaitingForTarget,
      targetFailed,
      completedAndRecycled,
      canRequest,
      onFileChange,
      requestTarget,
      uploadFix
    }
}

export type FixSubmitViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useFixSubmit>>>
