import { message as describeMessage } from '../../utils/i18n'
import { markRaw, ref } from 'vue'
import type { Ref } from 'vue'
import { ShieldCheck } from '@lucide/vue'
import TurnstileWidgetComponent from '@nuxtjs/turnstile/runtime/components/NuxtTurnstile.vue'
import { toast } from '../../utils/message-toast'
import type { MessageKey } from '~/locales/zh-CN'
import { createHumanVerificationCoordinator } from '~/lib/human-verification-coordinator'
import type { HumanVerificationHeaders } from '~/lib/human-verification-coordinator'

export type HumanVerificationAction = 'login' | 'registration' | 'runtime' | 'evaluation'
export type HumanVerificationSurface = 'dialog' | 'login-inline'

interface ChallengeState {
  id: number
  provider: 'Cap' | 'Turnstile'
  action: HumanVerificationAction
  surface: HumanVerificationSurface
  siteKey: string
  apiEndpoint: string
  progress: number
  errorKey: MessageKey | null
}

let nextChallengeId = 0
const requestCoordinator = createHumanVerificationCoordinator()
let capInstance: import('@cap.js/widget').Cap | null = null
const inlineCapReady = ref(false)
let inlineCapOwner: object | null = null
let challengeTimeout: ReturnType<typeof setTimeout> | null = null

function sharedChallenge() {
  return useState<ChallengeState | null>('human-verification:challenge', () => null)
}

function clearTimeoutHandle(): void {
  if (challengeTimeout) clearTimeout(challengeTimeout)
  challengeTimeout = null
}

function removeCapInstance(): void {
  inlineCapReady.value = false
  capInstance?.widget.remove()
  capInstance = null
}

function providerConfiguration(configuration: ReturnType<typeof usePlatform>['configuration']['value']) {
  return configuration?.humanVerification
}

function settleChallenge(
  challenge: Ref<ChallengeState | null>,
  headers: HumanVerificationHeaders | null,
): void {
  clearTimeoutHandle()
  if (headers && challenge.value?.surface === 'login-inline') {
    // Keep CAP's own expiration timer alive until the precomputed proof is consumed.
    inlineCapReady.value = true
  }
  else {
    removeCapInstance()
  }
  challenge.value = null
  requestCoordinator.settle(headers)
}

function failChallenge(
  challenge: Ref<ChallengeState | null>,
  errorKey: MessageKey,
  id = challenge.value?.id,
): void {
  if (!challenge.value || challenge.value.id !== id) return
  clearTimeoutHandle()
  removeCapInstance()
  challenge.value = { ...challenge.value, progress: 0, errorKey }
}

function beginChallengeTimeout(challenge: Ref<ChallengeState | null>, id: number): void {
  clearTimeoutHandle()
  challengeTimeout = setTimeout(() => {
    failChallenge(challenge, 'common.humanVerification.description.humanVerificationTimedOut', id)
  }, 120_000)
}

async function solveCapChallenge(
  challenge: Ref<ChallengeState | null>,
  id: number,
  complete: (token: string) => void,
  reject: (errorKey: MessageKey) => void,
): Promise<void> {
  const current = challenge.value
  if (current?.id !== id || current.provider !== 'Cap') return
  removeCapInstance()
  try {
    const { default: Cap } = await import('@cap.js/widget')
    if (challenge.value?.id !== id) return
    window.CAP_CUSTOM_WASM_URL = new URL(
      '../assets/cap_wasm_bg.wasm',
      current.apiEndpoint,
    ).toString()
    const workers = Math.max(1, Math.min(navigator.hardwareConcurrency || 4, 4))
    const instance = new Cap({
      apiEndpoint: current.apiEndpoint,
      'data-cap-worker-count': String(workers),
    })
    capInstance = instance
    instance.addEventListener('reset', () => {
      if (capInstance === instance) inlineCapReady.value = false
    })
    instance.addEventListener('progress', (event) => {
      if (challenge.value?.id === id)
        challenge.value = { ...challenge.value, progress: event.detail.progress }
    })
    const solved = await instance.solve()
    if (challenge.value?.id !== id) return
    if (!solved?.success || !solved.token) {
      reject('security.verification.failed')
      return
    }
    complete(solved.token)
  }
  catch {
    reject('security.verification.failed')
  }
}

/** Requests one provider token; an inline login proof can be prepared before submission. */
export function useHumanVerification() {
  const challenge = sharedChallenge()
  const { configuration, ensureLoaded } = usePlatform()
  let disposed = false
  const owner = {}

  onScopeDispose(() => {
    disposed = true
    if (inlineCapOwner !== owner) return
    inlineCapOwner = null
    if (challenge.value?.surface === 'login-inline') settleChallenge(challenge, null)
    else if (!challenge.value) removeCapInstance()
  })

  async function request(
    action: HumanVerificationAction,
    requestedSurface: HumanVerificationSurface = 'dialog',
  ): Promise<HumanVerificationHeaders | null> {
    await ensureLoaded()
    if (disposed) return null
    const provider = providerConfiguration(configuration.value)
    if (!provider?.provider) {
      toast.error(describeMessage('common.error.humanVerificationConfigurationUnavailable'))
      return null
    }
    if (action === 'runtime' && provider.runtimeRequired === false) return {}
    if (action === 'evaluation' && provider.evaluationRequired === false) return {}
    if (provider.provider === 'None') return {}
    if (requestCoordinator.active || challenge.value) {
      toast.info(describeMessage('common.humanVerification.description.anotherHumanVerificationProgress'))
      return null
    }

    const siteKey = provider.siteKey?.trim()
    const apiEndpoint = provider.apiEndpoint?.trim() ?? ''
    if (!siteKey || (provider.provider === 'Cap' && !apiEndpoint)) {
      toast.error(describeMessage('common.error.humanVerificationConfigurationUnavailable'))
      return null
    }

    const result = requestCoordinator.begin()
    if (!result) {
      toast.info(describeMessage('common.humanVerification.description.anotherHumanVerificationProgress'))
      return null
    }
    const id = ++nextChallengeId
    challenge.value = {
      id,
      provider: provider.provider,
      action,
      surface: provider.provider === 'Cap' && action === 'login' && requestedSurface === 'login-inline'
        ? 'login-inline'
        : 'dialog',
      siteKey,
      apiEndpoint,
      progress: 0,
      errorKey: null,
    }
    inlineCapOwner = challenge.value.surface === 'login-inline' ? owner : null
    beginChallengeTimeout(challenge, id)

    if (provider.provider === 'Cap') {
      void solveCapChallenge(
        challenge,
        id,
        token => settleChallenge(challenge, { 'X-NoCTF-Human-Verification': token }),
        errorKey => failChallenge(challenge, errorKey, id),
      )
    }
    return result
  }

  const inlineCap = computed(() => {
    const current = challenge.value
    if (current?.provider !== 'Cap' || current.surface !== 'login-inline') return null
    return {
      id: current.id,
      progress: Math.round(current.progress),
      errorMessage: current.errorKey ? translate(current.errorKey) : null,
    }
  })

  function retryInlineCap(): void {
    const current = challenge.value
    if (current?.provider !== 'Cap' || current.surface !== 'login-inline') return
    const id = ++nextChallengeId
    challenge.value = { ...current, id, progress: 0, errorKey: null }
    beginChallengeTimeout(challenge, id)
    void solveCapChallenge(
      challenge,
      id,
      token => settleChallenge(challenge, { 'X-NoCTF-Human-Verification': token }),
      errorKey => failChallenge(challenge, errorKey, id),
    )
  }

  function consumeInlineCap(headers: HumanVerificationHeaders): HumanVerificationHeaders | null {
    if (!inlineCapReady.value || capInstance?.token !== headers['X-NoCTF-Human-Verification']) return null
    removeCapInstance()
    return headers
  }

  return { request, inlineCap, inlineCapReady: computed(() => inlineCapReady.value), retryInlineCap, consumeInlineCap }
}

/** Owns the single application-level challenge surface rendered by ApplicationRoot. */
export function useHumanVerificationGate() {
  const challenge = sharedChallenge()
  const route = useRoute()

  function cancel(): void {
    settleChallenge(challenge, null)
  }

  function setOpen(open: boolean): void {
    if (!open) cancel()
  }

  function retry(): void {
    const current = challenge.value
    if (!current) return
    const id = ++nextChallengeId
    challenge.value = { ...current, id, progress: 0, errorKey: null }
    beginChallengeTimeout(challenge, id)
    if (current.provider === 'Cap') {
      void solveCapChallenge(
        challenge,
        id,
        token => settleChallenge(challenge, { 'X-NoCTF-Human-Verification': token }),
        errorKey => failChallenge(challenge, errorKey, id),
      )
    }
  }

  const turnstileToken = computed({
    get: () => '',
    set: (token: string) => {
      if (challenge.value?.provider === 'Turnstile' && token)
        settleChallenge(challenge, { 'X-NoCTF-Human-Verification': token })
    },
  })
  const turnstileOptions = computed(() => ({
    action: challenge.value?.action,
    appearance: 'interaction-only' as const,
    execution: 'render' as const,
    retry: 'never' as const,
    'refresh-expired': 'manual' as const,
    'refresh-timeout': 'manual' as const,
    language: currentLocale(),
    'response-field': false,
    'error-callback': () => failChallenge(challenge, 'security.verification.failed'),
    'expired-callback': () => failChallenge(challenge, 'common.humanVerification.description.humanVerificationTimedOut'),
    'timeout-callback': () => failChallenge(challenge, 'common.humanVerification.description.humanVerificationTimedOut'),
    'unsupported-callback': () => failChallenge(challenge, 'security.verification.failed'),
  }))

  watch(() => route.fullPath, () => {
    if (challenge.value) cancel()
  })
  onUnmounted(cancel)

  return {
    ShieldCheck: markRaw(ShieldCheck),
    TurnstileWidget: markRaw(TurnstileWidgetComponent),
    open: computed(() => challenge.value !== null && challenge.value.surface === 'dialog'),
    provider: computed(() => challenge.value?.provider ?? null),
    challengeId: computed(() => challenge.value?.id ?? 0),
    siteKey: computed(() => challenge.value?.siteKey ?? ''),
    progress: computed(() => Math.round(challenge.value?.progress ?? 0)),
    errorMessage: computed(() => challenge.value?.errorKey
      ? translate(challenge.value.errorKey)
      : null),
    turnstileToken,
    turnstileOptions,
    setOpen,
    cancel,
    retry,
  }
}

export type HumanVerificationGateViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useHumanVerificationGate>>
