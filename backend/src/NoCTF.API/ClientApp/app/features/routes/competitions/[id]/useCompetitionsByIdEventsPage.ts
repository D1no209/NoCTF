

import { adminGetCompetition, listCompetitionEvents } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse, NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol } from '../../../../api'
import { competitionEventHistoryRange } from '../../../../lib/competition-event-history'

type CompetitionEvent = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse

/** Owns state, effects and commands for CompetitionsByIdEventsPage. */
export function useCompetitionsByIdEventsPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const ctx = inject(competitionContextKey)!

  const { canOrganize, isAdministrator } = useAuth()

  const hasStaffHistory = ref(isAdministrator.value)

  const historyScopeResolved = ref(false)

  const historyScopeError = ref<string | null>(null)

  const initialKind = typeof route.query.kind === 'string' ? route.query.kind : 'all'

  const kind = ref<string>(initialKind)

  const kindOptions = [
    { value: 'all', label: "ui.allUpdates" },
    { value: 'CompetitionLifecycleChanged', label: "ui.gameStatus" },
    { value: 'AnnouncementPublished', label: "ui.announcement" }, { value: 'ChallengePublished', label: "ui.topicRelease" }, { value: 'HintPublished', label: "ui.promptRelease" },
    { value: 'FirstBloodAwarded', label: "ui.firstBlood" }, { value: 'SecondBloodAwarded', label: "ui.secondBlood" }, { value: 'ThirdBloodAwarded', label: "ui.thirdBlood" },
    { value: 'GameplayFactAdjudicated', label: "ui.submitReview" }, { value: 'TeamRegistered', label: "ui.teamRegistration" }, { value: 'TeamBanned', label: "ui.teamBan" },
    { value: 'QuestionOpened', label: "ui.consultingCreation" }, { value: 'QuestionReplied', label: "ui.consultationReply" }, { value: 'QuestionStatusChanged', label: "ui.consultationStatus" },
  ]

  const { items, loading, error, hasMore, initialized, loadMore, reset } =
    useCursorPagination<CompetitionEvent>(async (cursor) => {
      const competition = ctx.competition.value
      const range = competitionEventHistoryRange(
        hasStaffHistory.value,
        competition?.startTime,
      )
      const { data, error: err } = await listCompetitionEvents({
        path: { competitionId },
        query: {
          from: range.from,
          to: range.to,
          kind: kind.value === 'all' ? null : kind.value as NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
          cursor,
          limit: 50,
        },
      })
      if (err || !data) throw err ?? new Error(translate("ui.failedToLoad"))
      return { items: data.items, nextCursor: data.nextCursor }
    })

  async function resolveHistoryScope() {
    if (!hasStaffHistory.value && canOrganize.value) {
      const { data, error: requestError, response } = await adminGetCompetition({
        path: { competitionId },
      })
      hasStaffHistory.value = data?.administrationRole != null
      if (requestError && response?.status !== 403 && response?.status !== 404) {
        historyScopeError.value = parseApiError(
          requestError,
          translate("ui.couldNotConfirmAccessToTheFullEventHistoryShowing"),
        ).message
      }
    }
    historyScopeResolved.value = true
  }

  watch(kind, () => {
    reset()
    void loadMore()
  })

  function reload() {
    reset()
    void loadMore()
  }

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(competitionId, {
      competitionEventChanged: () => reload(),
    })
  })

  onUnmounted(() => unwatch?.())

  const levelVariant = (level?: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol) =>
    level === 'Error' ? ('destructive' as const) : level === 'Warning' ? ('secondary' as const) : ('outline' as const)

  const levelLabel = (level?: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol) => (level === 'Error' ? translate("ui.warning") : level === 'Warning' ? translate("ui.note") : translate("ui.information"))

  // Keep observing parent competition readiness after the initial scope request.
  watch(
    () => ctx.competition.value,
    (competition) => {
      if (competition && historyScopeResolved.value && !initialized.value) void loadMore()
    },
    { immediate: true },
  )

  async function initialize() {
    await resolveHistoryScope()
    if (ctx.competition.value && !initialized.value) await loadMore()
  }

  return {
      initialize,
      hasStaffHistory,
      historyScopeError,
      kind,
      kindOptions,
      items,
      loading,
      error,
      hasMore,
      initialized,
      loadMore,
      reload,
      levelVariant,
      levelLabel
    }
}

export type CompetitionsByIdEventsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdEventsPage>>>
