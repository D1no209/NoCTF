import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { ArrowLeft } from '@lucide/vue'
import { competitionChallengesPath } from '../../../../utils/app-routes'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'
import { createTrailingRefresh } from '../../../../lib/latest-page-refresh'
import { adminGetCompetition, listCompetitionEvents } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse, NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol } from '../../../../api'
import { competitionEventHistoryRange } from '../../../../lib/competition-event-history'

type CompetitionEvent = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse

/** Owns state, effects and commands for CompetitionsByIdEventsPage. */
export function useCompetitionsByIdEventsPage() {
  const route = useRoute()

  const competitionId = route.params.id as string

  const competitionReturnPath = competitionChallengesPath(competitionId)

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

  let latestEventAt = 0
  const pagination = useOffsetPagination<CompetitionEvent>(async ({ offset, limit, desc }) => {
    const competition = ctx.competition.value
    const range = competitionEventHistoryRange(
      hasStaffHistory.value,
      competition?.startTime,
      Math.max(Date.now(), latestEventAt),
    )
    const { data, error: err } = await listCompetitionEvents({
      path: { competitionId },
      query: {
        from: range.from,
        to: range.to,
        kind: kind.value === 'all' ? null : kind.value as NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
        offset,
        limit,
        desc,
      },
    })
    if (err || !data) throw err ?? new Error(translate("competitions.error.loadFailed"))
    return { items: data.items ?? [], total: data.total ?? 0 }
  }, { initialPageSize: 10, initialDesc: true })

  const { items, loading, error, initialized } = pagination
  const refreshEvents = createTrailingRefresh(() => pagination.loadPage())

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
    pagination.reset()
    void pagination.loadPage(1)
  })

  function reload() {
    return refreshEvents()
  }

  let unwatch: (() => void) | undefined

  onMounted(() => {
    unwatch = watchCompetition(competitionId, {
      competitionEventChanged: event => {
        const occurredAt = Date.parse(event.occurredAt)
        if (Number.isFinite(occurredAt)) latestEventAt = Math.max(latestEventAt, occurredAt)
        void reload()
      },
      onReconnected: () => void reload(),
    })
  })

  onUnmounted(() => {
    unwatch?.()
    pagination.reset()
  })

  const levelVariant = (level?: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol) =>
    level === 'Error' ? ('destructive' as const) : level === 'Warning' ? ('secondary' as const) : ('outline' as const)

  const levelLabel = (level?: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol) => (level === 'Error' ? translate("common.label.warning") : level === 'Warning' ? translate("competitions.label.note") : translate("common.label.information"))

  // Keep observing parent competition readiness after the initial scope request.
  watch(
    () => ctx.competition.value,
    (competition) => {
      if (competition && historyScopeResolved.value && !initialized.value) void pagination.loadPage(1)
    },
    { immediate: true },
  )

  async function initialize() {
    await resolveHistoryScope()
    if (ctx.competition.value && !initialized.value) await pagination.loadPage(1)
  }

  return {
      initialize,
      ArrowLeft,
      competitionReturnPath,
      hasStaffHistory,
      historyScopeError,
      kind,
      kindOptions,
      items,
      loading,
      error,
      initialized,
      page: pagination.page,
      pageCount: pagination.pageCount,
      total: pagination.total,
      pageLimit: pagination.limit,
      loadPage: pagination.loadPage,
      setPageSize: pagination.setPageSize,
      reload,
      levelVariant,
      levelLabel
    }
}

export type CompetitionsByIdEventsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdEventsPage>>>
