
import { ResponseMetadata, RequestPolicyOption } from '../../lib/api'

import { api } from '../../lib/api'
import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { inject, toRefs } from 'vue'


import type { NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse, NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse } from '../../api/models'
import { createLatestPageRefresh } from '../../lib/latest-page-refresh'
import { challengeGameplayFactStatusKey, createChallengeGameplayFactStatusReader } from './challenge-gameplay-fact-status'

type Submission = NoCTFAPIEndpointsGameplayFactsGameplayFactListItemResponse

/** Owns state, effects and commands for ChallengeSubmissionHistory. */
export function useChallengeSubmissionHistory(props: Readonly<Omit<{
  competitionId: string
  competitionChallengeId: string
  refreshKey?: number | null
}, "refreshKey"> & Required<Pick<{
  competitionId: string
  competitionChallengeId: string
  refreshKey?: number | null
}, "refreshKey">>>) {
  const readStatus = inject(challengeGameplayFactStatusKey) ?? createChallengeGameplayFactStatusReader()
  const valueDialogOpen = ref(false)

  const valueSubmission = ref<Submission | null>(null)

  const submittedValue = ref<string | null>(null)

  const valueLoading = ref(false)

  const valueError = ref<UiMessage | null>(null)

  let valueRequestGeneration = 0

  const { items, loading, error, hasMore, initialized, loadMore, reset } =
    useCursorPagination<Submission>(async (cursor) => {
      let requestError: unknown;
      const response = new ResponseMetadata();
      const data = await api.api.v1.competitions.byCompetitionId(props.competitionId).gameplayFacts.get({ queryParameters: {
          competitionChallengeId: props.competitionChallengeId,
          offset: cursor ? Number(cursor) || 0 : 0,
          limit: 20,
          desc: true,
        } , options: [new RequestPolicyOption({ response: response })] }).catch(cause => { requestError = cause; return undefined });
      if (response?.status === 404) return { items: [], nextCursor: null }
      if (requestError || !data) throw requestError ?? new Error(translate("challenges.challengeSubmission.error.loadSubmissionHistoryFailed"))
      const pageItems = data.items ?? []
      const offset = cursor ? Number(cursor) || 0 : 0
      const nextOffset = offset + pageItems.length
      return { items: pageItems, nextCursor: nextOffset < (data.total ?? 0) ? String(nextOffset) : null }
    })

  const { loadNextPage, refreshLatest } = createLatestPageRefresh({
    loadMore,
    reset: () => reset({ preserveItems: true }),
  })

  function sameId(
    left: string | null | undefined,
    right: string | null | undefined,
  ): boolean {
    if (!left || !right) return false
    return left.replaceAll('-', '').toLowerCase() === right.replaceAll('-', '').toLowerCase()
  }

  function applyStatus(status: NoCTFAPIEndpointsGameplayFactsGameplayFactStatusResponse): boolean {
    const submission = items.value.find(item => sameId(item.id, status.gameplayFactId))
    if (!submission) return false
    submission.state = status.state
    submission.result = status.result
    submission.failureCode = status.failureCode
    submission.updatedAt = status.updatedAt
    return true
  }

  async function refreshPending(): Promise<void> {
    const pending = items.value.filter(item => item.id && isGameplayFactPending(item.state))
    await Promise.all(pending.map(async (submission) => {
      const data = await readStatus(props.competitionId, submission.id!)
      applyStatus(data)
    }))
  }

  const {
    timedOut: pendingPollingTimedOut,
    error: pendingPollingError,
    start: startPendingPolling,
    stop: stopPendingPolling,
  } = usePolling(
    async () => {
      await refreshPending()
      return !items.value.some(item => isGameplayFactPending(item.state))
    },
    { interval: 1500, timeout: 300_000 },
  )

  const pendingPollingErrorMessage = computed(() => pendingPollingError.value
    ? parseApiError(pendingPollingError.value, describeMessage("common.flagSubmit.error.submissionStatusFailed")).displayMessage
    : null)

  watch(
    () => items.value.some(item => isGameplayFactPending(item.state)),
    pending => pending ? startPendingPolling() : stopPendingPolling(),
  )

  watch(
    () => [props.competitionId, props.competitionChallengeId] as const,
    () => {
      reset()
      void loadNextPage()
    },
    { immediate: true },
  )

  watch(
    () => props.refreshKey,
    (_value, previous) => {
      if (previous !== undefined) void refreshLatest()
    },
  )

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(props.competitionId, {
      gameplayFactStateChanged: payload => {
        if (!sameId(payload.competitionChallengeId, props.competitionChallengeId)) return
        if (!applyStatus(payload)) void refreshLatest()
      },
      onReconnected: () => void refreshLatest(),
    })
  })

  onUnmounted(() => {
    unwatch?.()
    stopPendingPolling()
  })

  function resultVariant(submission: Submission) {
    if (isGameplayFactPending(submission.state)) return 'secondary' as const
    return ['Correct', 'Applied', 'Unlocked', 'Controlled', 'ServiceUp'].includes(submission.result ?? '')
      ? ('default' as const)
      : ('destructive' as const)
  }

  function resultText(submission: Submission): string {
    if (isGameplayFactPending(submission.state)) return gameplayFactStateLabel(submission.state)
    const result = gameplayFactResultLabel(submission.result)
    return submission.failureCode
      ? `${result} · ${gameplayFactFailureCodeLabel(submission.failureCode)}`
      : result
  }

  function canReadSubmittedValue(submission: Submission): boolean {
    return submission.kind === 'FlagAttempt' || submission.kind === 'BreakAttempt'
  }

  function closeValueDialog(): void {
    valueRequestGeneration += 1
    valueDialogOpen.value = false
    valueSubmission.value = null
    submittedValue.value = null
    valueError.value = null
    valueLoading.value = false
  }

  async function openSubmittedValue(submission: Submission): Promise<void> {
    if (!submission.id || !canReadSubmittedValue(submission) || valueLoading.value) return
    const generation = ++valueRequestGeneration
    valueDialogOpen.value = true
    valueSubmission.value = submission
    submittedValue.value = null
    valueError.value = null
    valueLoading.value = true
    let requestError: unknown;
    const data = await api.api.v1.competitions.byCompetitionId(props.competitionId).gameplayFacts.byGameplayFactId(submission.id).value.get().catch(cause => { requestError = cause; return undefined });
    if (generation !== valueRequestGeneration) return
    valueLoading.value = false
    if (requestError || !data) {
      valueError.value = parseApiError(requestError, describeMessage("challenges.challengeSubmission.error.loadSubmittedFlagFailed")).displayMessage
      return
    }
    submittedValue.value = data.value ?? null
  }

  function setValueDialogOpen(open: boolean): void {
    if (!open) closeValueDialog()
  }

  return {
      ...toRefs(props),
      valueDialogOpen,
      valueSubmission,
      submittedValue,
      valueLoading,
      valueError,
      items,
      loading,
      error,
      hasMore,
      initialized,
      loadMore,
      loadNextPage,
      pendingPollingTimedOut,
      startPendingPolling,
      pendingPollingErrorMessage,
      resultVariant,
      resultText,
      canReadSubmittedValue,
      closeValueDialog,
      openSubmittedValue,
      setValueDialogOpen
    }
}

export type ChallengeSubmissionHistoryViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useChallengeSubmissionHistory>>>
