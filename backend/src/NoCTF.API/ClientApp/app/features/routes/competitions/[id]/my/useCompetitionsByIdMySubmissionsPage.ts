import { markRaw } from 'vue'

import { toast } from 'vue-sonner'
import { getGameplayFactStatusEndpoint, listChallengesEndpoint, listGameplayFactsEndpoint } from '../../../../../api'
import type { NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse } from '../../../../../api'
import CompetitionParticipantWorkspaceComponent from '../../../../competition/CompetitionParticipantWorkspace.vue'

type Submission = NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse

/** Owns state, effects and commands for CompetitionsByIdMySubmissionsPage. */
export function useCompetitionsByIdMySubmissionsPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const challengeTitles = ref<Record<string, string>>({})

  const challengeTitlesError = ref<string | null>(null)

  const { items, loading, error, hasMore, initialized, loadMore } =
    useCursorPagination<Submission>(async (cursor) => {
      const { data, error: err, response } = await listGameplayFactsEndpoint({
        path: { competitionId },
        query: { cursor, limit: 50 },
      })
      // 未加入队伍时后端返回 404:视为没有提交记录,展示空态而不是报错。
      if (response?.status === 404) {
        return { items: [], nextCursor: null }
      }
      if (err || !data) throw err ?? new Error(translate("ui.failedToLoad"))
      return { items: data.items, nextCursor: data.nextCursor }
    })

  async function loadChallengeTitles(): Promise<void> {
    challengeTitlesError.value = null
    const { data, error } = await listChallengesEndpoint({ path: { competitionId } })
    if (error || !data) {
      challengeTitlesError.value = parseApiError(error, translate("ui.failedToLoadChallengeNames")).message
      return
    }
    challengeTitles.value = Object.fromEntries(
      (data?.items ?? []).map((c) => [c.id!, c.title ?? '']),
    )
  }

  onMounted(() => void Promise.all([loadChallengeTitles(), loadMore()]))

  async function refreshPending() {
    const pending = items.value.filter((s) => isGameplayFactPending(s.state))
    await Promise.all(
      pending.map(async (submission) => {
        const { data, error: requestError } = await getGameplayFactStatusEndpoint({
          path: { competitionId, gameplayFactId: submission.id! },
        })
        if (requestError || !data)
          throw parseApiError(requestError, translate("ui.failedToRefreshSubmissionStatus"))
        submission.state = data.state
        submission.result = data.result
        submission.failureCode = data.failureCode
      }),
    )
  }

  const { timedOut, error: pollingError, start: startPolling } = usePolling(
    async () => {
      await refreshPending()
      return !items.value.some((s) => isGameplayFactPending(s.state))
    },
    { interval: 3000, timeout: 300_000 },
  )

  const pollingErrorMessage = computed(() => pollingError.value
    ? parseApiError(pollingError.value, translate("ui.failedToRefreshSubmissionStatus")).message
    : null)

  watch(
    () => items.value.some((s) => isGameplayFactPending(s.state)),
    (hasPending) => {
      if (hasPending) startPolling()
    },
  )

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(competitionId, {
      gameplayFactStateChanged: () => void refreshPending().catch((requestError) => {
        toast.error(parseApiError(requestError, translate("ui.failedToRefreshSubmissionStatus")).message)
        startPolling()
      }),
    })
  })

  onUnmounted(() => unwatch?.())

  function resultVariant(submission: Submission) {
    if (isGameplayFactPending(submission.state)) return 'secondary' as const
    return submission.result === 'Correct' ? ('default' as const) : ('destructive' as const)
  }

  function resultText(submission: Submission) {
    if (isGameplayFactPending(submission.state)) {
      return gameplayFactStateLabel(submission.state)
    }
    const result = gameplayFactResultLabel(submission.result)
    return submission.failureCode
      ? `${result} · ${gameplayFactFailureCodeLabel(submission.failureCode)}`
      : result
  }

  const CompetitionParticipantWorkspace = markRaw(CompetitionParticipantWorkspaceComponent)

  return {
      competitionId,
      challengeTitles,
      challengeTitlesError,
      items,
      loading,
      error,
      hasMore,
      initialized,
      loadMore,
      loadChallengeTitles,
      timedOut,
      startPolling,
      pollingErrorMessage,
      resultVariant,
      resultText,
      CompetitionParticipantWorkspace
    }
}

export type CompetitionsByIdMySubmissionsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdMySubmissionsPage>>>
