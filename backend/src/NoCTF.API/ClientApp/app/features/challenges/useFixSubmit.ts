import { toRefs } from 'vue'

import { toast } from 'vue-sonner'
import { requestAwdpDefenseTargetEndpoint, requestPatchVerificationTargetEndpoint, uploadPatchEndpoint, uploadPatchVerificationEndpoint } from '../../api'
import type { NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse, NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol, NoCtfapiEndpointsGameplayFactsPatchVerificationStateResponse, NoCtfapiEndpointsGameplayFactsPatchVerificationTargetFailureCodeProtocol, NoCtfapiEndpointsGameplayFactsPatchVerificationUploadFailureCode, NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol } from '../../api'
import { useHumanVerification } from '~/features/security/useHumanVerification'

/** Owns state, effects and commands for FixSubmit. */
export function useFixSubmit(props: Readonly<{
  competitionId: string
  competitionChallengeId: string
  defense?: NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse | NoCtfapiEndpointsGameplayFactsPatchVerificationStateResponse
  ctfPatchVerification?: boolean
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

  const patchRequestFailureLabels: Record<NoCtfapiEndpointsGameplayFactsPatchVerificationTargetFailureCodeProtocol, string> = {
    PatchVerificationNotAvailable: 'ui.thisCompetitionTeamOrChallengeDoesNotCurrentlyAllowA',
    ExperimentalFeatureDisabled: 'ui.patchVerificationIsDisabled',
    ActiveTargetExists: 'ui.aOneShotDefenseVerificationEnvironmentAlreadyExistsCompleteIt',
    PatchAlreadyVerified: 'ui.fixHasAlreadySucceededFurtherFixAttemptsAreNotAccepted',
    PatchAttemptsExhausted: 'ui.thisChallengeSFixAttemptsAreExhausted',
    RuntimeQuotaExceeded: 'ui.teamRuntimeQuotaReached',
    InvalidRuntimeConfiguration: 'ui.theOneShotDefenseVerificationEnvironmentIsMisconfiguredContactCompetition',
    TargetConcurrency: 'ui.theDefenseVerificationStateChangedRefreshAndTryAgain',
  }

  const patchUploadFailureLabels: Record<NoCtfapiEndpointsGameplayFactsPatchVerificationUploadFailureCode, string> = {
    ArchiveStreamNotSeekable: 'ui.theFixArchiveCannotBeValidatedSelectTheFileAgain',
    ArchiveTooLarge: 'ui.patchArchiveTooLarge',
    ArchiveInvalid: 'ui.theFixArchiveIsInvalidUploadAValidTarGz',
    TargetNotReady: 'ui.thisOneShotDefenseVerificationEnvironmentHasExpiredOrNo',
    PatchAlreadyVerified: 'ui.fixHasAlreadySucceededFurtherFixAttemptsAreNotAccepted',
    AttemptsExhausted: 'ui.thisChallengeSFixAttemptsAreExhausted',
    TargetConsumed: 'ui.thisOneShotDefenseVerificationEnvironmentAlreadyHasAFix',
    UploadConflict: 'ui.theDefenseVerificationStateChangedRefreshAndTryAgain',
  }

  const verificationState = computed(() => props.ctfPatchVerification
    ? (props.defense as NoCtfapiEndpointsGameplayFactsPatchVerificationStateResponse | undefined)?.verificationState
    : (props.defense as NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse | undefined)?.state)

  const hasGameplayFact = computed(() => props.ctfPatchVerification
    ? verificationState.value != null
    : Boolean((props.defense as NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse | undefined)?.gameplayFactId))

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
    && !hasGameplayFact.value,
  )

  const validating = computed(() =>
    verificationState.value === 'Pending'
    || verificationState.value === 'Queued'
    || verificationState.value === 'Processing'
  )

  const recycling = computed(() => props.defense?.runtimeState === 'Stopping')

  const patchWaitingForTarget = computed(() =>
    hasGameplayFact.value && targetCreating.value,
  )

  const targetFailed = computed(() => props.defense?.runtimeState === 'Failed')

  const completedAndRecycled = computed(() =>
    props.defense?.runtimeState === 'Stopped' && hasGameplayFact.value,
  )

  const canRequest = computed(() => !runtimeActive.value
    && (!props.ctfPatchVerification
      || ((props.defense as NoCtfapiEndpointsGameplayFactsPatchVerificationStateResponse | undefined)?.verificationResult !== 'Correct'
        && ((props.defense as NoCtfapiEndpointsGameplayFactsPatchVerificationStateResponse | undefined)?.remainingAttempts ?? 1) > 0)))

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
      const options = {
        headers: { ...targetCommandAttempt.headers({ competitionId: props.competitionId, challengeId: props.competitionChallengeId }), ...verificationHeaders },
        signal: AbortSignal.timeout(30_000),
        path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      }
      const { data, error } = props.ctfPatchVerification
        ? await requestPatchVerificationTargetEndpoint(options)
        : await requestAwdpDefenseTargetEndpoint(options)
      if (error || !data?.runtimeInstanceId) {
        const code = protocolCode<NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol
          | NoCtfapiEndpointsGameplayFactsPatchVerificationTargetFailureCodeProtocol>(error)
        const label = props.ctfPatchVerification
          ? patchRequestFailureLabels[code as NoCtfapiEndpointsGameplayFactsPatchVerificationTargetFailureCodeProtocol]
          : requestFailureLabels[code as NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol]
        toast.error(label ? translate(label) : parseApiError(error, translate("ui.failedToRequestTheDefenseVerificationEnvironment")).message)
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
      const options = {
        signal: AbortSignal.timeout(360_000),
        headers: { ...patchCommandAttempt.headers({ runtimeInstanceId, name: file.value.name, size: file.value.size, modified: file.value.lastModified }), ...verificationHeaders },
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
          runtimeInstanceId,
        },
        body: { file: file.value },
      }
      const { data, error } = props.ctfPatchVerification
        ? await uploadPatchVerificationEndpoint(options)
        : await uploadPatchEndpoint(options)
      if (error || !data?.gameplayFactId) {
        const code = protocolCode<NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol
          | NoCtfapiEndpointsGameplayFactsPatchVerificationUploadFailureCode>(error)
        const label = props.ctfPatchVerification
          ? patchUploadFailureLabels[code as NoCtfapiEndpointsGameplayFactsPatchVerificationUploadFailureCode]
          : uploadFailureLabels[code as NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol]
        toast.error(label ? translate(label) : parseApiError(error, translate("ui.fixUploadFailed")).message)
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
