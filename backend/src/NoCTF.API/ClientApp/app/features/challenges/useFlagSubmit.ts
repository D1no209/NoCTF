import { toRefs } from 'vue'

import { PartyPopper } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { getGameplayFactStatusEndpoint, judgeAwdpBreakFlag, judgePracticeFlag, submitFlagEndpoint } from '../../api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactAdmissionFailureCodeProtocol, NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse } from '../../api'

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
  const { user } = useAuth()
  const challengeKey = () => `${user.value?.userId ?? 'anonymous'}:${props.competitionId}:${props.competitionChallengeId}`
  const input = ref('')

  const commandAttempt = createCommandAttempt()

  const submitting = ref(false)

  const tracked = ref<TrackedSubmission[]>([])

  const toasted = new Set<string>()

  const celebrating = ref(false)

  const persistentResult = ref<{ correct: boolean | null; message: string } | null>(null)

  const solved = ref(props.initiallySolved || solvedChallengeKeys.has(challengeKey()))

  let requestGeneration = 0


  const remainingAttempts = ref<number | null>(props.remainingAttempts ?? null)

  let celebrationTimer: ReturnType<typeof setTimeout> | undefined


  const celebrationParticles = Array.from({ length: 20 }, (_, index) => ({
    id: index,
    angle: `${index * 18}deg`,
    distance: `-${3.5 + (index % 4) * 0.45}rem`,
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
    persistentResult.value = null
  })

  const attemptsExhausted = computed(() =>
    !props.practice && !props.readOnlyJudgement && remainingAttempts.value === 0)

  const inputDisabled = computed(() => attemptsExhausted.value || solved.value)

  function showResult(correct: boolean, message: string): void {
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
    { interval: 1500, timeout: 120_000 },
  )

  const pollingErrorMessage = computed(() => pollingError.value
    ? parseApiError(pollingError.value, translate("ui.failedToRefreshSubmissionStatus")).message
    : null)

  async function refreshOne(id: string): Promise<void> {
    const { data, error } = await getGameplayFactStatusEndpoint({
      path: { competitionId: props.competitionId, gameplayFactId: id },
    })
    if (error || !data)
      throw parseApiError(error, translate("ui.failedToRefreshSubmissionStatus"))
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
        celebrateCorrectFlag()
      }
      else {
        const result = gameplayFactResultLabel(data.result)
        const reason = data.failureCode ? gameplayFactFailureCodeLabel(data.failureCode) : null
        showResult(false, reason
          ? translate("ui.submissionJudgingCompleted2", { result, reason })
          : translate("ui.submissionJudgingCompleted", { result }))
      }
      emit('evaluated', data.result)
      tracked.value = tracked.value.filter(item => item.id !== id)
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
    if (props.readOnlyJudgement) {
      const { data, error } = await judgeAwdpBreakFlag({
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
        },
        body: { flag: lines[0]! },
        signal: AbortSignal.timeout(30_000),
      })
      if (generation !== requestGeneration) return
      if (error || !data) {
        const parsed = parseApiError(error, translate("ui.flagCheckFailed"))
        const message = parsed.code === 'AchievementNotSucceeded'
          ? translate("ui.theBreakAchievementHasNotSucceededYet")
          : parsed.code === 'JudgementUnavailable'
            ? translate("ui.readOnlyFlagCheckingIsUnavailableForThisChallenge")
            : parsed.code === 'FlagInvalid'
              ? translate("ui.theFlagFormatIsInvalid")
              : parsed.status === 403
                ? translate("ui.theCurrentAccountHasNoApprovedTeamEligibleForThis")
                : parsed.message
        persistentResult.value = { correct: null, message }
        return
      }
      if (data.result === 'Correct') {
        showResult(true, translate("ui.flagIsCorrectThisCheckCreatesNoCompetitionRecords"))
        celebrateCorrectFlag()
      }
      else {
        showResult(false, translate("ui.incorrectFlag"))
      }
      return
    }
    if (props.practice) {
      const { data, error } = await judgePracticeFlag({
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
        },
        body: { flag: lines[0]! },
        signal: AbortSignal.timeout(30_000),
      })
      if (generation !== requestGeneration) return
      if (error || !data) {
        const parsed = parseApiError(error, translate("ui.failedToVerifyThePracticeFlag"))
        const message = parsed.code === 'PracticeUnavailable'
          ? translate("ui.practiceModeIsNotEnabledForThisCompetition")
          : parsed.code === 'RuntimeNotRunning'
            ? translate("ui.startTheChallengeEnvironmentAndWaitUntilItIsRunning")
            : parsed.code === 'FlagInvalid'
              ? translate("ui.theFlagFormatIsInvalid")
              : parsed.status === 403
                ? translate("ui.thisAccountDoesNotBelongToAnApprovedTeamEligible")
                : parsed.message
        persistentResult.value = { correct: null, message }
        return
      }
      if (data.result !== 'Correct' && data.result !== 'Wrong') {
        persistentResult.value = { correct: null, message: translate("ui.theJudgingEndpointReturnedNoValidResultPleaseRetry") }
        return
      }
      if (data.result === 'Correct') {
        input.value = ''
        showResult(true, translate("ui.correctFlagPracticeAttemptsDoNotAwardPoints"))
        celebrateCorrectFlag()
      }
      else {
        showResult(false, translate("ui.incorrectFlag"))
      }
      return
    }
    const { data, error } = await submitFlagEndpoint({
      signal: AbortSignal.timeout(30_000),
      headers: commandAttempt.headers({ competitionId: props.competitionId, challengeId: props.competitionChallengeId, lines }),
      path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      body: props.multiple ? { flags: lines } : { flag: lines[0] },
    })
    if (generation !== requestGeneration) return
    if (error || !data) {
      const parsed = parseApiError(error, translate("ui.submissionFailed"))
      const code = parsed.code as NoCtfapiEndpointsGameplayFactsGameplayFactAdmissionFailureCodeProtocol | undefined
      if (code === 'AttemptsExhausted') {
        remainingAttempts.value = 0
        emit('remainingChanged', 0)
      }
      persistentResult.value = { correct: null, message: code === 'AchievementAlreadySucceeded'
        ? translate("ui.breakHasAlreadySucceededUseVerificationModeToCheckWhether")
        : parsed.message }
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
    if (!ids.length) throw new Error(translate("ui.theJudgingEndpointReturnedNoValidResultPleaseRetry"))
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
        persistentResult.value = { correct: null, message: parseApiError(error, translate("ui.flagCheckFailed")).message }
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

    stopPolling()
  })

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(props.competitionId, {
      gameplayFactStateChanged: (payload) => {
        const id = competitionHubString(payload, 'gameplayFactId')
        if (id && tracked.value.some((t) => t.id === id)) {
          void refreshOne(id).catch((error) => {
            toast.error(parseApiError(error, translate("ui.failedToRefreshSubmissionStatus")).message)
            startPolling()
          })
        }
      },
    })
  })

  onUnmounted(() => {
    unwatch?.()
    stopPolling()
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
