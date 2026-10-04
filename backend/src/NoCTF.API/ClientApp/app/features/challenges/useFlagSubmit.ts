import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { inject, toRefs } from 'vue'

import { PartyPopper } from '@lucide/vue'
import { toast } from '../../utils/message-toast'
import { judgeAwdpBreakFlag, submitFlagEndpoint } from '../../api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactAdmissionFailureCodeProtocol, NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse } from '../../api'
import { useHumanVerification } from '~/features/security/useHumanVerification'
import { challengeGameplayFactStatusKey, createChallengeGameplayFactStatusReader } from './challenge-gameplay-fact-status'

type TrackedSubmission = Pick<
  NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse,
  'state' | 'result' | 'failureCode'
> & {
  id: string
}

const solvedChallengeKeys = new Set<string>()

/** Owns state, effects and commands for FlagSubmit. */
export function useFlagSubmit(props: Readonly<Omit<{
    competitionId: string
    competitionChallengeId: string
    /** AWD 批量提交:多行输入,一次提交多个 flag */
    multiple?: boolean
    title?: string
    description?: string
    practice?: boolean
    readOnlyJudgement?: boolean
    dockTarget?: string
    maximumAttempts?: number | null
    remainingAttempts?: number | null
    initiallySolved?: boolean
  }, "multiple" | "title" | "description" | "practice" | "readOnlyJudgement" | "dockTarget" | "initiallySolved"> & Required<Pick<{
    competitionId: string
    competitionChallengeId: string
    /** AWD 批量提交:多行输入,一次提交多个 flag */
    multiple?: boolean
    title?: string
    description?: string
    practice?: boolean
    readOnlyJudgement?: boolean
    dockTarget?: string
    maximumAttempts?: number | null
    remainingAttempts?: number | null
    initiallySolved?: boolean
  }, "multiple" | "title" | "description" | "practice" | "readOnlyJudgement" | "dockTarget" | "initiallySolved">>>,
emit: { (event: "evaluated", ...args: [result: TrackedSubmission['result']]): void; (event: "submitted", ...args: [gameplayFactIds: string[]]): void; (event: "remainingChanged", ...args: [remaining: number | null]): void }) {
  const readStatus = inject(challengeGameplayFactStatusKey) ?? createChallengeGameplayFactStatusReader()
  const { user } = useAuth()
  const { request: requestHumanVerification } = useHumanVerification()
  const challengeKey = () =>
    `${user.value?.userId ?? 'anonymous'}:${props.competitionId}:${props.competitionChallengeId}:${props.practice ? 'practice' : 'official'}`
  const input = ref('')

  const commandAttempt = createCommandAttempt()

  const submitting = ref(false)

  const tracked = ref<TrackedSubmission[]>([])

  const toasted = new Set<string>()

  const pendingRefreshes = new Map<string, Promise<void>>()

  const celebrating = ref(false)

  const persistentResult = ref<{ correct: boolean | null; message: UiMessage } | null>(null)

  const solved = ref(props.initiallySolved || solvedChallengeKeys.has(challengeKey()))

  let requestGeneration = 0


  const remainingAttempts = ref<number | null>(props.remainingAttempts ?? null)

  let celebrationTimer: ReturnType<typeof setTimeout> | undefined


  const celebrationParticles = Array.from({ length: 20 }, (_, index) => ({
    id: index,
    angle: `${index * 18}deg`,
    distance: `-${5 + (index % 4) * 0.65}rem`,
    delay: `${(index % 5) * 18}ms`,
    tone: index % 3 === 0
      ? 'text-destructive'
      : index % 3 === 1 ? 'text-primary' : 'text-foreground',
  }))

  watch(() => props.remainingAttempts, value => {
    remainingAttempts.value = value ?? null
  })

  watch(() => props.initiallySolved, value => {
    if (!value) return
    solvedChallengeKeys.add(challengeKey())
    solved.value = true
    input.value = ''
    if (persistentResult.value?.correct !== true) persistentResult.value = null
  })

  const attemptsExhausted = computed(() =>
    !props.practice && !props.readOnlyJudgement && remainingAttempts.value === 0)

  const inputDisabled = computed(() => solved.value || attemptsExhausted.value)

  function showResult(correct: boolean, message: UiMessage): void {
    persistentResult.value = { correct, message }
  }

  function celebrateCorrectFlag(): void {
    celebrating.value = false
    if (celebrationTimer) clearTimeout(celebrationTimer)
    requestAnimationFrame(() => {
      celebrating.value = true
      celebrationTimer = setTimeout(() => {
        celebrating.value = false
      }, 1000)
    })
  }

  const { timedOut, error: pollingError, start: startPolling, stop: stopPolling } = usePolling(
    async () => {
      await refreshPending()
      return !tracked.value.some((t) => isGameplayFactPending(t.state))
    },
    { interval: 1500, delays: [350, 1500], timeout: 120_000 },
  )

  const pollingErrorMessage = computed(() => pollingError.value
    ? parseApiError(pollingError.value, describeMessage("common.flagSubmit.error.submissionStatusFailed")).displayMessage
    : null)

  async function refreshOne(id: string): Promise<void> {
    const active = pendingRefreshes.get(id)
    if (active) return active
    const generation = requestGeneration
    const refresh = loadOne(id, generation)
    pendingRefreshes.set(id, refresh)
    try {
      await refresh
    }
    finally {
      if (pendingRefreshes.get(id) === refresh) pendingRefreshes.delete(id)
    }
  }

  async function loadOne(id: string, generation: number): Promise<void> {
    const data = await readStatus(props.competitionId, id)
    if (generation !== requestGeneration) return
    const item = tracked.value.find((t) => t.id === id)
    const wasPending = item ? isGameplayFactPending(item.state) : true
    if (item) {
      item.state = data.state
      item.result = data.result
      item.failureCode = data.failureCode
    }
    else {
      tracked.value.unshift({
        id,
        state: data.state,
        result: data.result,
        failureCode: data.failureCode,
      })
    }
    if (wasPending && !isGameplayFactPending(data.state) && !toasted.has(id)) {
      toasted.add(id)
      if (data.result === 'Correct') {
        solvedChallengeKeys.add(challengeKey())
        solved.value = true
        if (props.practice)
          showResult(true, translate("challenges.flagSubmit.description.correctFlagPracticeAttempts"))
        else
          showResult(true, translate('terminal.challengeSolved'))
        celebrateCorrectFlag()
      }
      else {
        const result = gameplayFactResultLabel(data.result)
        const reason = data.failureCode ? gameplayFactFailureCodeLabel(data.failureCode) : null
        showResult(false, reason
          ? translate("challenges.label.submissionJudgingCompleted.useFlagSubmit", { result, reason })
          : translate("challenges.label.submissionJudgingCompleted", { result }))
      }
      emit('evaluated', data.result)
      tracked.value = tracked.value.filter(item => item.id !== id)
      if (!tracked.value.some(item => isGameplayFactPending(item.state))) stopPolling()
    }
  }

  async function refreshPending(): Promise<void> {
    const pending = tracked.value.filter((t) => isGameplayFactPending(t.state))
    await Promise.all(pending.map((t) => refreshOne(t.id)))
  }

  async function submit() {
    if (submitting.value || inputDisabled.value) return
    const generation = requestGeneration
    persistentResult.value = null
    try {
    const lines = props.multiple
      ? input.value.split('\n').map((line) => line.trim()).filter(Boolean)
      : [input.value.trim()].filter(Boolean)
    if (!lines.length) return
    submitting.value = true
    const verificationHeaders = await requestHumanVerification('evaluation')
    if (verificationHeaders === null) return
    if (props.readOnlyJudgement) {
      const { data, error } = await judgeAwdpBreakFlag({
        headers: verificationHeaders,
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
        },
        body: { flag: lines[0]! },
        signal: AbortSignal.timeout(30_000),
      })
      if (generation !== requestGeneration) return
      if (error || !data) {
        const parsed = parseApiError(error, describeMessage("challenges.error.flagCheckFailed"))
        const message = parsed.code === 'AchievementNotSucceeded'
          ? translate("challenges.flagSubmit.description.breakAchievementSucceededYet")
          : parsed.code === 'JudgementUnavailable'
            ? translate("challenges.flagSubmit.error.readFlagCheckingUnavailable")
            : parsed.code === 'FlagInvalid'
              ? translate("challenges.flagSubmit.error.flagFormatInvalid")
              : parsed.status === 403
                ? translate("challenges.flagSubmit.description.accountApprovedTeamEligible")
                : parsed.displayMessage
        persistentResult.value = { correct: null, message }
        return
      }
      if (data.result === 'Correct') {
        showResult(true, translate("challenges.flagSubmit.description.flagCorrectCheckCreates"))
        celebrateCorrectFlag()
      }
      else {
        showResult(false, translate("challenges.error.flagFailed"))
      }
      return
    }
    const { data, error } = await submitFlagEndpoint({
      signal: AbortSignal.timeout(30_000),
      headers: { ...commandAttempt.headers({ competitionId: props.competitionId, challengeId: props.competitionChallengeId, lines }), ...verificationHeaders },
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      body: props.multiple ? { flags: lines } : { flag: lines[0] },
    })
    if (generation !== requestGeneration) return
    if (error || !data) {
      const parsed = parseApiError(error, describeMessage("challenges.error.submissionFailed"))
      const code = parsed.code as NoCtfapiEndpointsGameplayFactsGameplayFactAdmissionFailureCodeProtocol | undefined
      if (code === 'AttemptsExhausted') {
        remainingAttempts.value = 0
        emit('remainingChanged', 0)
      }
      persistentResult.value = { correct: null, message: code === 'AchievementAlreadySucceeded'
        ? translate("challenges.flagSubmit.description.breakAlreadySucceededVerification")
        : code === 'RuntimeNotRunning'
          ? translate("challenges.flagSubmit.description.startChallengeEnvironmentWait")
          : parsed.displayMessage }
      return
    }
    const ids = [
      ...(data.gameplayFactId ? [data.gameplayFactId] : []),
      ...(data.submissions ?? []).map((s) => s.gameplayFactId).filter((id): id is string => !!id),
    ]
    for (const id of ids) {
      if (!tracked.value.some((t) => t.id === id)) {
        tracked.value.unshift({ id, state: 'Pending', result: null })
      }
    }
    if (!ids.length) throw new Error(translate("challenges.flagSubmit.description.judgingEndpointReturnedValid"))
    commandAttempt.completed()
    input.value = ''
    if (remainingAttempts.value !== null) {
      remainingAttempts.value = Math.max(0, remainingAttempts.value - ids.length)
      emit('remainingChanged', remainingAttempts.value)
    }
    emit('submitted', ids)
    startPolling()
    } catch (error) {
      if (generation === requestGeneration) {
        persistentResult.value = { correct: null, message: parseApiError(error, describeMessage("challenges.error.flagCheckFailed")).displayMessage }
      }
    } finally {
      if (generation === requestGeneration) submitting.value = false
    }
  }

  watch(() => [user.value?.userId, props.competitionId, props.competitionChallengeId, props.practice], () => {
    requestGeneration++
    submitting.value = false
    persistentResult.value = null
    solved.value = props.initiallySolved || solvedChallengeKeys.has(challengeKey())
    input.value = ''
    tracked.value = []
    pendingRefreshes.clear()

    stopPolling()
  })

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(props.competitionId, {
      gameplayFactStateChanged: (payload) => {
        const id = competitionHubString(payload, 'gameplayFactId')
        if (id && tracked.value.some((t) => t.id === id)) {
          void refreshOne(id).catch((error) => {
            toast.error(parseApiError(error, describeMessage("common.flagSubmit.error.submissionStatusFailed")).displayMessage)
            startPolling()
          })
        }
      },
    })
  })

  onUnmounted(() => {
    unwatch?.()
    stopPolling()
    pendingRefreshes.clear()
    if (celebrationTimer) clearTimeout(celebrationTimer)

  })

  const viewBindings = {
      ...toRefs(props),
      PartyPopper,
      input,
      submitting,
      celebrating,
      persistentResult,
      solved,

      remainingAttempts,
      celebrationParticles,
      attemptsExhausted,
      inputDisabled,

      timedOut,
      pollingErrorMessage,
      submit
    }
  return viewBindings
}

export type FlagSubmitViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useFlagSubmit>>>
