import { toRefs } from 'vue'

import { getGameplayFactStatusEndpoint, getGameplayFactValueEndpoint, listGameplayFactsEndpoint } from '../../api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse, NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse } from '../../api'
import { createLatestPageRefresh } from '../../lib/latest-page-refresh'

type Submission = NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse

/** Owns state, effects and commands for ChallengeSubmissionHistory. */
export function useChallengeSubmissionHistory(props: Readonly<Omit<{
  competitionId: string
  competitionChallengeId: string
  refreshKey?: number
}, "refreshKey"> & Required<Pick<{
  competitionId: string
  competitionChallengeId: string
  refreshKey?: number
}, "refreshKey">>>) {
  const valueDialogOpen = ref(false)

  const valueSubmission = ref<Submission | null>(null)

  const submittedValue = ref<string | null>(null)

  const valueLoading = ref(false)

  const valueError = ref<string | null>(null)

  let valueRequestGeneration = 0

  const { items, loading, error, hasMore, initialized, loadMore, reset } =
    useCursorPagination<Submission>(async (cursor) => {
      const { data, error: requestError, response } = await listGameplayFactsEndpoint({
        path: { competitionId: props.competitionId },
        query: {
          competitionChallengeId: props.competitionChallengeId,
          offset: cursor ? Number(cursor) || 0 : 0,
          limit: 20,
          desc: true,
        },
      })
      if (response?.status === 404) return { items: [], nextCursor: null }
      if (requestError || !data) throw requestError ?? new Error(translate("ui.failedToLoadSubmissionHistory"))
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

  function applyStatus(status: NoCtfapiEndpointsGameplayFactsGameplayFactStatusResponse): boolean {
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
      const { data, error: requestError } = await getGameplayFactStatusEndpoint({
        path: {
          competitionId: props.competitionId,
          gameplayFactId: submission.id!,
        },
      })
      if (requestError || !data)
        throw parseApiError(requestError, translate("ui.failedToRefreshSubmissionStatus"))
      if (data) applyStatus(data)
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
    ? parseApiError(pendingPollingError.value, translate("ui.failedToRefreshSubmissionStatus")).message
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
    const { data, error: requestError } = await getGameplayFactValueEndpoint({
      path: {
        competitionId: props.competitionId,
        gameplayFactId: submission.id,
      },
    })
    if (generation !== valueRequestGeneration) return
    valueLoading.value = false
    if (requestError || !data) {
      valueError.value = parseApiError(requestError, translate("ui.failedToLoadTheSubmittedFlagValue")).message
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
