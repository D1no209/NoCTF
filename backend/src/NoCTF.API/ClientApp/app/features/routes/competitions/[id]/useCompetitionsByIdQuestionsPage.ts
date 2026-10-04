
import { api } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { toast } from '../../../../utils/message-toast'

import type { NoCTFAPIEndpointsChallengesChallengeSummaryResponse, NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode, NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionSubjectCode, NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionResponse } from '../../../../api/models'
import { createLatestRequestGuard } from '../../../../lib/latest-request'
import { competitionQuestionErrorMessage, competitionQuestionRoleLabel, isCompetitionQuestionHandlerRole, mergeCompetitionQuestions } from '../../../../lib/competition-question'
import { createTrailingRefresh } from '../../../../lib/latest-page-refresh'
import { maximumQuestionBodyLength, maximumQuestionTitleLength, minimumQuestionBodyLength, minimumQuestionTitleLength, validateCompetitionQuestionDraft } from '../../../../lib/participant-form-validation'
import CompetitionParticipantWorkspaceComponent from '../../../competition/CompetitionParticipantWorkspace.vue'

type Question = NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionResponse

/** Owns state, effects and commands for CompetitionsByIdQuestionsPage. */
export function useCompetitionsByIdQuestionsPage() {
  const route = useRoute()

  const router = useRouter()

  const competitionId = route.params.id as string
  const { user } = useAuth()
  const isOwnMessage = (actorUserId?: string | null) => Boolean(actorUserId && actorUserId === user.value?.userId)

  const { markRead, unreadCount } = useCompetitionQuestionReadState(competitionId)

  const refreshError = ref<UiMessage | null>(null)

  async function fetchQuestionPage(cursor: string | null) {
    let error: unknown;
    const data = await api.api.v1.competitions.byCompetitionId(competitionId).questions.get({ queryParameters: { cursor: cursor ?? undefined, limit: 50 } }).catch(cause => { error = cause; return undefined });
    if (error || !data)
      throw parseApiError(error, describeMessage("notifications.competitionsBy.error.loadConsultationListFailed"))
    return { items: data.items ?? [], nextCursor: data.nextCursor ?? null }
  }

  const {
    items: questions,
    loading,
    error: paginationError,
    hasMore,
    initialized,
    loadMore,
  } = useCursorPagination<Question>(fetchQuestionPage)

  const listError = computed(() => refreshError.value ?? paginationError.value?.message ?? null)

  async function loadMoreQuestions() {
    refreshError.value = null
    await loadMore()
    questions.value = mergeCompetitionQuestions([], questions.value)
  }

  const refreshList = createTrailingRefresh(async () => {
    if (!initialized.value) {
      await loadMoreQuestions()
      return
    }

    refreshError.value = null
    try {
      const page = await fetchQuestionPage(null)
      questions.value = mergeCompetitionQuestions(questions.value, page.items ?? [])
      paginationError.value = null
    }
    catch (error) {
      refreshError.value = parseApiError(error, describeMessage("notifications.competitionsBy.error.loadConsultationListFailed")).displayMessage
    }
  })

  function upsertQuestion(question: Question) {
    questions.value = mergeCompetitionQuestions(questions.value, [question])
  }

  const createOpen = ref(false)

  const createSubject = ref<NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionSubjectCode>('Challenge')

  const createChallengeId = ref<string>('none')

  const createTitle = ref('')

  const createBody = ref('')

  const createPending = ref(false)

  const createError = ref<UiMessage | null>(null)

  const challenges = ref<NoCTFAPIEndpointsChallengesChallengeSummaryResponse[]>([])

  const challengesLoading = ref(false)

  const challengeLoadError = ref<UiMessage | null>(null)

  async function loadChallengeOptions(): Promise<void> {
    if (challengesLoading.value) return
    challengesLoading.value = true
    challengeLoadError.value = null
    let error: unknown;
    const data = await api.api.v1.competitions.byCompetitionId(competitionId).challenges.get().catch(cause => { error = cause; return undefined });
    challengesLoading.value = false
    if (error || !data) {
      challengeLoadError.value = parseApiError(error, describeMessage("notifications.competitionsBy.error.loadAvailableChallengesFailed")).displayMessage
      return
    }
    challenges.value = (data?.items ?? []).filter((c) => c.isPublished)
  }

  watch(createOpen, (open) => {
    if (open && !challenges.value.length && !challengeLoadError.value)
      void loadChallengeOptions()
  })

  async function submitCreate() {
    if (createPending.value) return
    createError.value = validateCompetitionQuestionDraft({
      requiresChallenge: createSubject.value === 'Challenge',
      challengeId: createChallengeId.value === 'none' ? null : createChallengeId.value,
      title: createTitle.value,
      body: createBody.value,
    })
    if (createError.value) return

    createPending.value = true
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).questions.post({
          subject: createSubject.value,
          competitionChallengeId:
            createSubject.value === 'Challenge' && createChallengeId.value !== 'none'
              ? createChallengeId.value
              : null,
          title: createTitle.value.trim(),
          body: createBody.value.trim(),
        }).catch(cause => { error = cause; return undefined });
      if (error || !data) {
        createError.value = competitionQuestionErrorMessage(error, translate("notifications.error.submitConsultationFailed"))
        toast.error(createError.value)
        return
      }
      toast.success(describeMessage("notifications.label.inquirySubmitted"))
      createOpen.value = false
      createTitle.value = ''
      createBody.value = ''
      createError.value = null
      upsertQuestion(data)
      await select(data.threadRootId!)
    }
    catch (error) {
      createError.value = competitionQuestionErrorMessage(error, translate("notifications.error.submitConsultationFailed"))
      toast.error(createError.value)
    }
    finally {
      createPending.value = false
    }
  }

  function setCreateOpen(open: boolean) {
    if (createPending.value) return
    createOpen.value = open
    if (!open) createError.value = null
  }

  const selectedId = ref<string | null>(null)

  const detail = ref<Question | null>(null)

  const detailLoading = ref(false)

  const detailRequests = createLatestRequestGuard()

  function applyDetailQuestion(question: Question, markAsRead = true) {
    const currentDetail = detail.value
    const current = currentDetail && currentDetail.threadRootId === question.threadRootId
      ? [currentDetail]
      : []
    const fresh = mergeCompetitionQuestions(current, [question])[0] ?? question
    detail.value = fresh
    upsertQuestion(fresh)
    if (markAsRead) markRead(fresh)
  }

  async function select(id: string, syncRoute = true) {
    if (detailLoading.value && selectedId.value === id) return
    if (selectedId.value !== id) { detail.value = null; reply.value = ''; replyError.value = null }
    const request = detailRequests.begin()
    selectedId.value = id
    detailLoading.value = true
    if (syncRoute && route.query.question !== id) {
      void router.replace({ query: { ...route.query, question: id } })
    }
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).questions.byThreadRootId(id).get().catch(cause => { error = cause; return undefined });
      if (!detailRequests.isCurrent(request) || selectedId.value !== id) return
      if (error || !data) {
        toast.error(parseApiError(error, describeMessage("notifications.competitionsBy.error.loadConsultationDetailsFailed")).displayMessage)
        return
      }
      applyDetailQuestion(data)
    }
    catch (error) {
      if (detailRequests.isCurrent(request))
        toast.error(parseApiError(error, describeMessage("notifications.competitionsBy.error.loadConsultationDetailsFailed")).displayMessage)
    }
    finally {
      if (detailRequests.isCurrent(request))
        detailLoading.value = false
    }
  }

  watch(() => route.query.question, async (value) => {
    const questionId = typeof value === 'string' ? value : null
    if (questionId && questionId !== selectedId.value)
      await select(questionId, false)
  })

  const refreshSelectedDetail = createTrailingRefresh(async () => {
    const questionId = selectedId.value
    if (!questionId) return

    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).questions.byThreadRootId(questionId).get().catch(cause => { error = cause; return undefined });
      if (error || !data || selectedId.value !== questionId) return
      applyDetailQuestion(data, document.visibilityState === 'visible')
    }
    catch {
      // Background refresh failures keep the currently displayed thread intact.
    }
  })

  async function refreshFromServer() {
    await Promise.all([refreshList(), refreshSelectedDetail()])
  }

  let unwatchCompetition: (() => void) | undefined

  let disposed = false

  onMounted(async () => {
    const questionId = typeof route.query.question === 'string' ? route.query.question : null
    await Promise.all([
      loadMoreQuestions(),
      questionId ? select(questionId, false) : Promise.resolve(),
    ])
    if (disposed) return
    unwatchCompetition = watchCompetition(competitionId, {
      competitionEventChanged: () => void refreshFromServer(),
      onReconnected: () => void refreshFromServer(),
    })
  })

  onUnmounted(() => {
    disposed = true
    detailRequests.invalidate()
    unwatchCompetition?.()
  })

  const reply = ref('')

  const replyPending = ref(false)

  const replyError = ref<UiMessage | null>(null)

  async function submitReply() {
    if (replyPending.value || !reply.value.trim() || !detail.value?.canReply) return
    const questionId = detail.value.threadRootId!
    const submittedReply = reply.value
    replyError.value = null
    replyPending.value = true
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).questions.byThreadRootId(questionId).messages.post({ body: submittedReply.trim() }).catch(cause => { error = cause; return undefined });
      if (error || !data) {
        replyError.value = competitionQuestionErrorMessage(error, translate("notifications.error.sendingFailed"))
        toast.error(replyError.value)
        return
      }
      if (selectedId.value === questionId) {
        applyDetailQuestion(data)
        if (reply.value === submittedReply) reply.value = ''
      } else upsertQuestion(data)
      toast.success(describeMessage("notifications.label.messageSent"))
    }
    catch (error) {
      replyError.value = competitionQuestionErrorMessage(error, translate("notifications.error.sendingFailed"))
      toast.error(replyError.value)
    }
    finally {
      replyPending.value = false
    }
  }

  const statusPending = ref(false)

  async function changeStatus(status: 'Resolved' | 'Closed') {
    if (!detail.value || statusPending.value) return
    const questionId = detail.value.threadRootId!
    if (status === 'Resolved' ? !detail.value.canResolve : !detail.value.canClose) return
    statusPending.value = true
    try {
      let error: unknown;
      const data = await api.api.v1.competitions.byCompetitionId(competitionId).questions.byThreadRootId(questionId).status.put({ status }).catch(cause => { error = cause; return undefined });
      if (error || !data) {
        toast.error(competitionQuestionErrorMessage(error, translate("notifications.error.statusUpdateFailed")))
        return
      }
      if (selectedId.value === questionId) applyDetailQuestion(data)
      else upsertQuestion(data)
      toast.success(status === 'Resolved' ? translate("notifications.label.advisoryMarkedResolved") : translate("notifications.label.inquiryClosed"))
    }
    catch (error) {
      toast.error(competitionQuestionErrorMessage(error, translate("notifications.error.statusUpdateFailed")))
    }
    finally {
      statusPending.value = false
    }
  }

  const statusVariant = (status?: string | null) =>
    status === 'Pending'
      ? ('secondary' as const)
      : status === 'Replied'
        ? ('default' as const)
        : ('outline' as const)

  const statusLabel = (status?: string | null) =>
    ({ Pending: translate("notifications.label.awaitingReply"), Replied: translate("notifications.label.replied"), Resolved: translate("notifications.label.resolved"), Closed: translate("notifications.label.closed") })[status ?? ''] ?? status

  const roleLabel = (role?: NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode | null) =>
    role ? translate(competitionQuestionRoleLabel[role]) : translate("notifications.label.unknownRole")

  const isHandlerRole = (role?: NoCTFAPIEndpointsChallengesQuestionsCompetitionQuestionParticipantRoleCode | null) =>
    isCompetitionQuestionHandlerRole(role)

  const participantLimitReached = computed(() =>
    detail.value?.access === 'Asker'
    && detail.value.status !== 'Closed'
    && (detail.value.participantMessagesRemaining ?? 0) <= 0,
  )

  const CompetitionParticipantWorkspace = markRaw(CompetitionParticipantWorkspaceComponent)

  const viewBindings = {
      maximumQuestionBodyLength,
      maximumQuestionTitleLength,
      minimumQuestionBodyLength,
      minimumQuestionTitleLength,
      competitionId,
      isOwnMessage,
      unreadCount,
      questions,
      loading,
      hasMore,
      initialized,
      loadMore,
      listError,
      loadMoreQuestions,
      createOpen,
      createSubject,
      createChallengeId,
      createTitle,
      createBody,
      createPending,
      createError,
      challenges,
      challengesLoading,
      challengeLoadError,
      loadChallengeOptions,
      submitCreate,
      setCreateOpen,
      selectedId,
      detail,
      detailLoading,
      select,
      reply,
      replyPending,
      replyError,
      submitReply,
      statusPending,
      changeStatus,
      statusVariant,
      statusLabel,
      roleLabel,
      isHandlerRole,
      participantLimitReached,
      CompetitionParticipantWorkspace
    }
  const viewState = proxyRefs(viewBindings)

  function onInputCreateError(value: typeof viewState.createError) {
    viewState.createError = value
  }

  function onInputReplyError(value: typeof viewState.replyError) {
    viewState.replyError = value
  }

  return { ...viewBindings, onInputCreateError, onInputReplyError }
}

export type CompetitionsByIdQuestionsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionsByIdQuestionsPage>>>
