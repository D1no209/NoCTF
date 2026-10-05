import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'


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

  const historyScopeError = ref<UiMessage | null>(null)

  const initialKind = typeof route.query.kind === 'string' ? route.query.kind : 'all'

  const kind = ref<string>(initialKind)

  const kindOptions = [
    { value: 'all', label: "competitions.label.updates" },
    { value: 'CompetitionLifecycleChanged', label: "common.label.gameStatus" },
    { value: 'AnnouncementPublished', label: "common.label.announcement" }, { value: 'ChallengePublished', label: "common.label.topicRelease" }, { value: 'HintPublished', label: "common.label.promptRelease" },
    { value: 'FirstBloodAwarded', label: "common.label.firstBlood" }, { value: 'SecondBloodAwarded', label: "common.label.secondBlood" }, { value: 'ThirdBloodAwarded', label: "common.label.thirdBlood" },
    { value: 'GameplayFactAdjudicated', label: "common.label.submitReview" }, { value: 'TeamRegistered', label: "common.label.teamRegistration" }, { value: 'TeamBanned', label: "common.label.teamBan" },
    { value: 'QuestionOpened', label: "common.label.consultingCreation" }, { value: 'QuestionReplied', label: "common.label.consultationReply" }, { value: 'QuestionStatusChanged', label: "common.label.consultationStatus" },
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
          offset: 0,
          limit: 50,
          desc: true,
        },
      })
      if (err || !data) throw err ?? new Error(translate("competitions.error.loadFailed"))
      return { items: data.items, nextCursor: data.nextCursor }
    })

  async function resolveHistoryScope() {
    if (!hasStaffHistory.value && canOrganize.value) {
      const { data, error: requestError, response } = await adminGetCompetition({
        path: { competitionId },
      })
      hasStaffHistory.value = data?.competition?.administrationRole != null
      if (requestError && response?.status !== 403 && response?.status !== 404) {
        historyScopeError.value = parseApiError(
          requestError,
          describeMessage("competitions.competitionsBy.description.couldConfirmAccessFull"),
        ).displayMessage
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

  const levelLabel = (level?: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol) => (level === 'Error' ? translate("common.label.warning") : level === 'Warning' ? translate("competitions.label.note") : translate("common.label.information"))

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
