import { useSubmissionTiming } from './timing/useSubmissionTiming'
import { message as describeMessage } from '../../utils/i18n'
import { toRefs } from 'vue'

import { toast } from '../../utils/message-toast'
import { requestAwdpDefenseTargetEndpoint, requestPatchVerificationTargetEndpoint, uploadPatchEndpoint, uploadPatchVerificationEndpoint } from '../../api'
import type { NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse, NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol, NoCtfapiEndpointsGameplayFactsPatchVerificationStateResponse, NoCtfapiEndpointsGameplayFactsPatchVerificationTargetFailureCodeProtocol, NoCtfapiEndpointsGameplayFactsPatchVerificationUploadFailureCode, NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol } from '../../api'
import { useHumanVerification } from '~/features/security/useHumanVerification'

/** Owns state, effects and commands for FixSubmit. */
export function useFixSubmit(props: Readonly<{
  competitionId: string
  competitionChallengeId: string
  defense?: NoCtfapiEndpointsGameplayFactsAwdpDefenseProgressResponse | NoCtfapiEndpointsGameplayFactsPatchVerificationStateResponse
  timing?: import("~/api").NoCtfapiEndpointsChallengesCompetitionChallengeTimingResponse | null
  ctfPatchVerification?: boolean
}>,
emit: { (event: "changed", ...args: []): void; (event: "accepted", ...args: []): void }) {
  const { request: requestHumanVerification } = useHumanVerification()
  const file = ref<File | null>(null)

  const pendingAction = ref<'request' | 'upload' | null>(null)

  const requestFailureLabels: Record<NoCtfapiEndpointsGameplayFactsAwdpDefenseTargetRequestFailureCodeProtocol, string> = {
    DefenseNotAvailable: "common.fixSubmit.description.competitionTeamChallengeCurrently",
    ActiveDefenseTargetExists: "common.fixSubmit.description.oneShotDefenseVerification",
    DefenseAlreadySucceeded: "common.fixSubmit.description.fixAlreadySucceededFurther",
    BreakRequired: "common.fixSubmit.description.challengeRequiresValidBreak",
    FixAttemptsExhausted: "common.fixSubmit.description.challengeSFixAttempts",
    InvalidRuntimeConfiguration: "common.fixSubmit.description.oneShotDefenseVerification.useFixSubmit",
    DefenseTargetConcurrency: "common.fixSubmit.description.defenseVerificationStateChanged",
  }

  const uploadFailureLabels: Record<NoCtfapiEndpointsGameplayFactsUploadPatchFailureCodeProtocol, string> = {
    ArchiveStreamNotSeekable: "common.fixSubmit.validation.fixArchiveFormat",
    ArchiveInvalid: "common.fixSubmit.error.fixArchiveUploadInvalid",
    DefenseTargetNotReady: "common.fixSubmit.description.oneShotDefenseVerification.verificationEnvironmentExpired",
    DefenseAlreadySucceeded: "common.fixSubmit.description.fixAlreadySucceededFurther",
    FixAttemptsExhausted: "common.fixSubmit.description.challengeSFixAttempts",
    DefenseTargetConsumed: "common.fixSubmit.description.oneShotDefenseVerification.environmentAlreadyFix",
  }

  const patchRequestFailureLabels: Record<NoCtfapiEndpointsGameplayFactsPatchVerificationTargetFailureCodeProtocol, string> = {
    PatchVerificationNotAvailable: 'common.fixSubmit.description.competitionTeamChallengeCurrently',
    ExperimentalFeatureDisabled: 'common.label.patchVerificationDisabled',
    ActiveTargetExists: 'common.fixSubmit.description.oneShotDefenseVerification',
    PatchAlreadyVerified: 'common.fixSubmit.description.fixAlreadySucceededFurther',
    PatchAttemptsExhausted: 'common.fixSubmit.description.challengeSFixAttempts',
    RuntimeQuotaExceeded: 'common.label.teamRuntimeQuotaReached',
    InvalidRuntimeConfiguration: 'common.fixSubmit.description.oneShotDefenseVerification.useFixSubmit',
    TargetConcurrency: 'common.fixSubmit.description.defenseVerificationStateChanged',
  }

  const patchUploadFailureLabels: Record<NoCtfapiEndpointsGameplayFactsPatchVerificationUploadFailureCode, string> = {
    ArchiveStreamNotSeekable: 'common.fixSubmit.validation.fixArchiveFormat',
    ArchiveTooLarge: 'common.label.patchArchiveTooLarge',
    ArchiveInvalid: 'common.fixSubmit.error.fixArchiveUploadInvalid',
    TargetNotReady: 'common.fixSubmit.description.oneShotDefenseVerification.verificationEnvironmentExpired',
    PatchAlreadyVerified: 'common.fixSubmit.description.fixAlreadySucceededFurther',
    AttemptsExhausted: 'common.fixSubmit.description.challengeSFixAttempts',
    TargetConsumed: 'common.fixSubmit.description.oneShotDefenseVerification.environmentAlreadyFix',
    UploadConflict: 'common.fixSubmit.description.defenseVerificationStateChanged',
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

  const { submissionsClosed } = useSubmissionTiming(() => props.timing)
  const canUpload = computed(() => !submissionsClosed.value &&
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

  const canRequest = computed(() => !submissionsClosed.value && !runtimeActive.value
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
        toast.error(label ? translate(label) : parseApiError(error, describeMessage("challenges.fixSubmit.error.defenseVerificationEnvironmentFailed")).displayMessage)
        return
      }
      targetCommandAttempt.completed()
      toast.success(describeMessage("challenges.fixSubmit.description.oneShotDefenseVerification"))
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
        toast.error(label ? translate(label) : parseApiError(error, describeMessage("challenges.error.fixUploadFailed")).displayMessage)
        return
      }
      patchCommandAttempt.completed()
      file.value = null
      toast.success(describeMessage("challenges.fixSubmit.description.fixLockedOneShot"))
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
