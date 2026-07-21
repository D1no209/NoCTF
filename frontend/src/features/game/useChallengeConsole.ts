import { computed, onUnmounted, ref, watch } from 'vue'
import { ApiError, competitionApi } from '@/api/noctf'
import { normalizeDirection } from '@/lib/challengeDirections'
import type {
  AwdpChallengeStateDto,
  CompetitionChallengeDto,
} from '@/features/competitions/useCompetitionDetailPage'

export type ChallengeConsoleChallengeDto = CompetitionChallengeDto
export type ChallengeConsoleAwdpStateDto = AwdpChallengeStateDto

export interface ChallengeConsoleInstanceDto {
  containerId?: string | null
  ports?: Record<string, number>
  addresses?: string[]
  address?: string | null
  accessHost?: string
  status?: string
  expiresAt?: string | null
  cooldownUntil?: string | null
  serverTime?: string
}

export interface ChallengeConsoleFlagResponseDto {
  correct?: boolean
  alreadySolved?: boolean
  result?: string | null
  message?: string | null
}

export type ChallengeFlagFailureKind
  = | 'attempts_exhausted'
    | 'instance_required'
    | 'instance_expired'
    | 'submission_failed'

export type ChallengeFlagOutcome
  = | { kind: 'correct', alreadySolved?: boolean }
    | { kind: 'incorrect', message?: string, reason?: string }
    | { kind: 'failed', message?: string }
    | { kind: 'skipped' }

export type ChallengeInstanceFailureKind = 'create' | 'destroy' | 'extend'

export type ChallengeInstanceOutcome
  = | { kind: 'ok' }
    | { kind: 'failed' }
    | { kind: 'skipped' }

export type ChallengePatchOutcome
  = | { kind: 'ok' }
    | { kind: 'failed' }
    | { kind: 'skipped' }

export type AwdpBlockedReason
  = | 'instance_required'
    | 'attack_exhausted'
    | 'defense_exhausted'
    | 'break_locked'
    | 'fix_locked'
    | ''

export interface UseChallengeConsoleOptions {
  competitionId: () => string
  challenge: () => ChallengeConsoleChallengeDto | null
  open: () => boolean
  isAwdMode?: () => boolean | undefined
  isAwdpMode?: () => boolean | undefined
  defenseEnabled?: () => boolean | undefined
  canCreateInstance?: () => boolean | undefined
  canSubmitFlag?: () => boolean | undefined
  canRequestDefense?: () => boolean | undefined
  awdpState?: () => ChallengeConsoleAwdpStateDto | null | undefined
}

function readOption(read: (() => boolean | undefined) | undefined) {
  return read ? read() : undefined
}

// Canonical behavior follows V1's ChallengeModal: instance polling every 10s
// while the console is open on a dynamic-container challenge, a 1s clock
// driving cooldown/expiry math, and outcome objects instead of user-facing
// copy so each theme renders its own feedback.
export function useChallengeConsole(options: UseChallengeConsoleOptions) {
  const flagInput = ref('')
  const submitting = ref(false)
  const submitResult = ref<'correct' | 'incorrect' | null>(null)
  const submitFailureKind = ref<ChallengeFlagFailureKind | null>(null)
  const patchFile = ref<File | null>(null)
  const patchUploading = ref(false)
  const instanceCreating = ref(false)
  const instanceDestroying = ref(false)
  const instanceExtending = ref(false)
  const instanceLoading = ref(false)
  const instance = ref<ChallengeConsoleInstanceDto | null>(null)
  const instanceFailureKind = ref<ChallengeInstanceFailureKind | null>(null)
  const instanceFailureDetail = ref('')
  const nowMs = ref(Date.now())
  let clockTimer: ReturnType<typeof setInterval> | null = null
  let pollTimer: ReturnType<typeof setInterval> | null = null

  const isDynamicContainer = computed(() => {
    const dt = options.challenge()?.deploymentType
    if (dt === 2) return true
    if (typeof dt === 'string' && dt.toLowerCase() === 'dynamiccontainer') return true
    const dir = normalizeDirection(options.challenge()?.typeId)
    if (dir === 'WEB' || dir === 'PWN') return true
    return false
  })
  const canCreateInstanceOption = computed(() => readOption(options.canCreateInstance))
  const showContainerControls = computed(() => Boolean(isDynamicContainer.value && canCreateInstanceOption.value !== false))
  const canCreateDynamicInstance = computed(() => Boolean(showContainerControls.value))
  const canSubmitCurrentFlag = computed(() => readOption(options.canSubmitFlag) !== false)
  const canRequestCurrentDefense = computed(() => readOption(options.canRequestDefense) !== false)
  const runningInstance = computed(() => instance.value?.status === 'running' && Boolean(instance.value?.containerId))
  const instanceAddress = computed(() => instance.value?.address ?? instance.value?.addresses?.[0] ?? '')
  const expiresAtMs = computed(() => instance.value?.expiresAt ? new Date(instance.value.expiresAt).getTime() : null)
  const cooldownUntilMs = computed(() => instance.value?.cooldownUntil ? new Date(instance.value.cooldownUntil).getTime() : null)
  const expiresInMs = computed(() => expiresAtMs.value ? Math.max(0, expiresAtMs.value - nowMs.value) : 0)
  const cooldownMs = computed(() => cooldownUntilMs.value ? Math.max(0, cooldownUntilMs.value - nowMs.value) : 0)
  const isCoolingDown = computed(() => cooldownMs.value > 0)
  const canOperateInstance = computed(() => Boolean(canCreateDynamicInstance.value && !isCoolingDown.value))
  const hasInstanceOperation = computed(() => instanceCreating.value || instanceDestroying.value || instanceExtending.value || instanceLoading.value)
  const canUploadPatch = computed(() => Boolean(
    readOption(options.isAwdMode)
    && readOption(options.defenseEnabled)
    && patchFile.value
    && options.challenge()
    && canRequestCurrentDefense.value,
  ))
  const awdpState = computed(() => options.awdpState?.() ?? null)
  const awdpInstanceRunning = computed(() => {
    if (runningInstance.value) return true
    return awdpState.value?.instanceStatus === 'InstanceRunning'
  })
  const awdpRemainingAttackAttempts = computed(() => awdpState.value?.remainingAttackAttempts ?? 0)
  const awdpRemainingDefenseAttempts = computed(() => awdpState.value?.remainingDefenseAttempts ?? 0)
  const awdpAttackAttemptsLabel = computed(() => `${awdpRemainingAttackAttempts.value} / ${awdpState.value?.maxAttackAttempts ?? 0}`)
  const awdpDefenseAttemptsLabel = computed(() => `${awdpRemainingDefenseAttempts.value} / ${awdpState.value?.maxDefenseAttempts ?? 0}`)
  const awdpCanSubmitFlag = computed(() => Boolean(
    options.challenge()
    && awdpInstanceRunning.value
    && canSubmitCurrentFlag.value
    && (awdpState.value?.canSubmitFlag ?? true),
  ))
  const awdpCanRequestDefense = computed(() => Boolean(
    options.challenge()
    && awdpInstanceRunning.value
    && canRequestCurrentDefense.value
    && (awdpState.value?.canRequestDefense ?? true),
  ))
  const canUploadAwdpFix = computed(() => Boolean(
    readOption(options.isAwdpMode)
    && options.challenge()
    && patchFile.value
    && awdpCanRequestDefense.value,
  ))
  const awdpBlockedReason = computed<AwdpBlockedReason>(() => {
    if (!awdpInstanceRunning.value) return 'instance_required'
    if (awdpState.value?.breakStatus === 'AttackAttemptsExhausted') return 'attack_exhausted'
    if (awdpState.value?.fixStatus === 'DefenseAttemptsExhausted') return 'defense_exhausted'
    if (awdpState.value?.breakStatus === 'BreakSuccess' && awdpState.value.allowAttackAfterBreakSuccess === false) return 'break_locked'
    if (awdpState.value?.fixStatus === 'FixSuccess' && awdpState.value.allowDefenseAfterFixSuccess === false) return 'fix_locked'
    return ''
  })

  function resetConsole() {
    flagInput.value = ''
    submitResult.value = null
    submitFailureKind.value = null
    patchFile.value = null
    instanceFailureKind.value = null
    instanceFailureDetail.value = ''
    stopInstancePolling()
  }

  watch(
    () => [options.open(), options.challenge()?.id, isDynamicContainer.value, canCreateInstanceOption.value] as const,
    ([open]) => {
      if (open && isDynamicContainer.value && options.challenge() && canCreateInstanceOption.value !== false) {
        startClock()
        void refreshInstance()
        startInstancePolling()
      }
      else {
        stopInstancePolling()
      }
    },
    { immediate: true },
  )

  onUnmounted(() => {
    stopInstancePolling()
    stopClock()
  })

  function recordInstanceFailure(kind: ChallengeInstanceFailureKind, error: unknown) {
    instanceFailureKind.value = kind
    instanceFailureDetail.value = getApiErrorDetail(error)
  }

  async function createInstance(): Promise<ChallengeInstanceOutcome> {
    const challenge = options.challenge()
    if (!challenge || !canOperateInstance.value)
      return { kind: 'skipped' }
    instanceCreating.value = true
    instanceFailureKind.value = null
    instanceFailureDetail.value = ''
    try {
      instance.value = await competitionApi.createInstance<ChallengeConsoleInstanceDto>(
        options.competitionId(),
        challenge.id,
      )
      return { kind: 'ok' }
    }
    catch (error) {
      recordInstanceFailure('create', error)
      return { kind: 'failed' }
    }
    finally {
      instanceCreating.value = false
    }
  }

  async function refreshInstance() {
    const challenge = options.challenge()
    if (!challenge || !isDynamicContainer.value || canCreateInstanceOption.value === false)
      return
    instanceLoading.value = true
    try {
      instance.value = await competitionApi.getInstance<ChallengeConsoleInstanceDto>(options.competitionId(), challenge.id)
    }
    catch {
      instance.value = null
    }
    finally {
      instanceLoading.value = false
    }
  }

  async function destroyInstance(): Promise<ChallengeInstanceOutcome> {
    const challenge = options.challenge()
    if (!challenge || !runningInstance.value || !canOperateInstance.value)
      return { kind: 'skipped' }
    instanceDestroying.value = true
    instanceFailureKind.value = null
    instanceFailureDetail.value = ''
    try {
      instance.value = await competitionApi.destroyInstance<ChallengeConsoleInstanceDto>(options.competitionId(), challenge.id)
      return { kind: 'ok' }
    }
    catch (error) {
      recordInstanceFailure('destroy', error)
      return { kind: 'failed' }
    }
    finally {
      instanceDestroying.value = false
    }
  }

  async function extendInstance(): Promise<ChallengeInstanceOutcome> {
    const challenge = options.challenge()
    if (!challenge || !runningInstance.value || !canOperateInstance.value)
      return { kind: 'skipped' }
    instanceExtending.value = true
    instanceFailureKind.value = null
    instanceFailureDetail.value = ''
    try {
      instance.value = await competitionApi.extendInstance<ChallengeConsoleInstanceDto>(options.competitionId(), challenge.id)
      return { kind: 'ok' }
    }
    catch (error) {
      recordInstanceFailure('extend', error)
      return { kind: 'failed' }
    }
    finally {
      instanceExtending.value = false
    }
  }

  async function copyInstanceAddress() {
    if (!instanceAddress.value)
      return false
    try {
      await navigator.clipboard.writeText(instanceAddress.value)
      return true
    }
    catch {
      return false
    }
  }

  async function submitFlag(): Promise<ChallengeFlagOutcome> {
    const challenge = options.challenge()
    if (!challenge || !canSubmitCurrentFlag.value || !flagInput.value.trim())
      return { kind: 'skipped' }
    submitting.value = true
    submitResult.value = null
    submitFailureKind.value = null

    try {
      const data = await competitionApi.submitFlag<ChallengeConsoleFlagResponseDto>(
        options.competitionId(),
        challenge.id,
        flagInput.value.trim(),
      )

      if (data?.correct) {
        submitResult.value = 'correct'
        return { kind: 'correct', alreadySolved: data.alreadySolved }
      }

      if (data?.result === 'attempts_exhausted') {
        submitFailureKind.value = 'attempts_exhausted'
      }
      else if (data?.result === 'instance_required') {
        submitFailureKind.value = 'instance_required'
      }
      else if (data?.result === 'instance_expired') {
        submitFailureKind.value = 'instance_expired'
      }
      else {
        submitResult.value = 'incorrect'
      }
      return { kind: 'incorrect', message: data?.message ?? undefined, reason: data?.result ?? undefined }
    }
    catch (error) {
      submitFailureKind.value = 'submission_failed'
      return { kind: 'failed', message: error instanceof Error ? error.message : undefined }
    }
    finally {
      submitting.value = false
    }
  }

  async function submitPatch(): Promise<ChallengePatchOutcome> {
    const challenge = options.challenge()
    if (!challenge || !patchFile.value)
      return { kind: 'skipped' }
    const awdpMode = Boolean(readOption(options.isAwdpMode))
    if (awdpMode && !awdpCanRequestDefense.value)
      return { kind: 'skipped' }
    if (!awdpMode && (!readOption(options.defenseEnabled) || !canRequestCurrentDefense.value))
      return { kind: 'skipped' }
    patchUploading.value = true

    try {
      await competitionApi.submitPatch(options.competitionId(), challenge.id, patchFile.value)
      patchFile.value = null
      return { kind: 'ok' }
    }
    catch {
      return { kind: 'failed' }
    }
    finally {
      patchUploading.value = false
    }
  }

  function startClock() {
    if (clockTimer) return
    nowMs.value = Date.now()
    clockTimer = setInterval(() => {
      nowMs.value = Date.now()
      if (runningInstance.value && expiresInMs.value <= 0) void refreshInstance()
    }, 1000)
  }

  function stopClock() {
    if (!clockTimer) return
    clearInterval(clockTimer)
    clockTimer = null
  }

  function startInstancePolling() {
    if (pollTimer) return
    pollTimer = setInterval(() => {
      if (options.open() && isDynamicContainer.value) void refreshInstance()
    }, 10_000)
  }

  function stopInstancePolling() {
    if (!pollTimer) return
    clearInterval(pollTimer)
    pollTimer = null
  }

  return {
    flagInput,
    submitting,
    submitResult,
    submitFailureKind,
    patchFile,
    patchUploading,
    instance,
    instanceCreating,
    instanceDestroying,
    instanceExtending,
    instanceLoading,
    instanceFailureKind,
    instanceFailureDetail,
    isDynamicContainer,
    showContainerControls,
    canCreateDynamicInstance,
    canSubmitCurrentFlag,
    canRequestCurrentDefense,
    runningInstance,
    instanceAddress,
    expiresInMs,
    cooldownMs,
    isCoolingDown,
    canOperateInstance,
    hasInstanceOperation,
    canUploadPatch,
    awdpState,
    awdpInstanceRunning,
    awdpAttackAttemptsLabel,
    awdpDefenseAttemptsLabel,
    awdpCanSubmitFlag,
    awdpCanRequestDefense,
    canUploadAwdpFix,
    awdpBlockedReason,
    resetConsole,
    createInstance,
    refreshInstance,
    destroyInstance,
    extendInstance,
    copyInstanceAddress,
    submitFlag,
    submitPatch,
  }
}

function getApiErrorDetail(error: unknown) {
  if (error instanceof ApiError) {
    if (typeof error.details === 'string') return error.details
    if (error.details && typeof error.details === 'object') return JSON.stringify(error.details)
    if (error.status) return `HTTP ${error.status}`
  }
  return error instanceof Error ? error.message : ''
}
