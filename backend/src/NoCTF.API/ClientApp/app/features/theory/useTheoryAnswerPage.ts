import { getCompetitionTheory, getTheorySession, issueTheoryPaper, answerTheoryPaper, submitTheoryPaper, getTheoryExamLeaderboard } from '~/api'
import type { NoCtfApplicationTheoryPapersTheoryPaperView as PaperView, NoCtfApplicationTheoryPapersTheoryParticipantQuestion as Question,
  NoCtfApplicationTheoryActivitiesTheoryExamLeaderboard as ExamBoard } from '~/api'
import { parseApiError } from '~/utils/api-error'
import { message, localeTag, t, type UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import { useTheoryAssets } from './useTheoryAssets'
import { questionKindLabel, scoringLabel } from './theory-labels'
import { watchNotifications } from '~/composables/useNotificationHub'
import { acknowledgeAnswer, acceptRemoteAnswer, createAnswer, overwriteRemoteAnswer, prepareAnswer, reconcileAnswer, selectAnswer, type AnswerDraft, type SavedAnswer } from './answer-state'

export function useTheoryAnswerPage() {
  const route = useRoute(), router = useRouter()
  const competitionId = computed(() => typeof route.params.id === 'string' ? route.params.id : null)
  const activityId = ref(typeof route.params.activityId === 'string' ? route.params.activityId : '')
  const view = ref<PaperView | null>(null), loading = ref(false), busy = ref(false), error = ref<UiMessage | null>(null)
  const drafts = reactive<Record<string, AnswerDraft>>({}), marks = reactive(new Set<string>()), current = ref(0), files = useTheoryAssets()
  const sheetOpen = ref(false), submitOpen = ref(false), confirmation = ref<{ paperId: string; revision: string } | null>(null)
  const clock = ref(Date.now()), offset = ref(0), boardOpen = ref(false), board = ref<ExamBoard | null>(null)
  const boardPage = ref(1), boardLimit = ref(20), boardLoading = ref(false), lostUnsynced = ref(false)
  const leaveOpen = ref(false); let leaveResolver: ((value: boolean) => void) | undefined
  let epoch = 0, mutation = 0, disposed = false, interval: ReturnType<typeof setInterval> | undefined, saveTimer: ReturnType<typeof setTimeout> | undefined
  let readPending = false, draining: Promise<void> | null = null, issueRequest = '', tickCount = 0
  let unwatch: (() => void) | undefined
  const queue = new Set<string>()
  const questions = computed(() => view.value?.paper?.questions ?? [])
  const selected = computed(() => questions.value[current.value] ?? null)
  const draft = computed(() => selected.value?.id ? drafts[selected.value.id] ?? null : null)
  const canAnswer = computed(() => view.value?.canAnswer === true && !busy.value)
  const unsaved = computed(() => Object.values(drafts).some(draft => draft.dirty || draft.saving || draft.pending !== null))
  const answered = computed(() => questions.value.filter(question => question.id && (drafts[question.id]?.selected.length ?? 0) > 0).length)
  const answerItems = computed(() => questions.value.map((question, index) => ({ value: String(index),
    label: `${t('theory.questionNumber', { number: index + 1 })} · ${question.id && (drafts[question.id]?.selected.length ?? 0) > 0 ? t('theory.answered') : t('theory.unanswered')}${question.id && marks.has(question.id) ? ' ⚑' : ''}` })))
  const remaining = computed(() => {
    const at = new Date(view.value?.deadline?.at ?? '').getTime()
    const now = view.value?.deadline?.paused ? new Date(view.value?.serverTime ?? '').getTime() : clock.value + offset.value
    return Math.max(0, Math.ceil((at - now) / 1000)) || 0
  })
  const remainingLabel = computed(() => `${Math.floor(remaining.value / 60).toString().padStart(2, '0')}:${(remaining.value % 60).toString().padStart(2, '0')}`)
  function saved(question: Question): SavedAnswer { return { revision: question.answerRevision ?? '', selected: (question.options ?? []).filter(option => option.selected && option.id).map(option => option.id!) } }
  function receive(data: PaperView) {
    const changedPaper = view.value?.paper?.id !== data.paper?.id
    if (changedPaper) lostUnsynced.value = false
    else if (view.value?.paper?.state === 0 && data.paper?.state !== 0 && unsaved.value) lostUnsynced.value = true
    if (changedPaper || data.paper?.state !== 0) { Object.keys(drafts).forEach(id => delete drafts[id]); queue.clear(); marks.clear(); files.clear() }
    view.value = data
    if (data.paper?.state === 0) issueRequest = ''
    if (data.serverTime) offset.value = new Date(data.serverTime).getTime() - Date.now()
    for (const question of data.paper?.questions ?? []) {
      if (!question.id || !question.answerRevision) continue
      const existing = drafts[question.id]
      if (existing) reconcileAnswer(existing, saved(question)); else drafts[question.id] = createAnswer(saved(question))
    }
    current.value = Math.min(Math.max(0, changedPaper ? Number(route.query.q ?? 1) - 1 : current.value), Math.max(0, questions.value.length - 1))
    if (data.paper?.state !== 0) { submitOpen.value = false; confirmation.value = null }
    if (data.paper?.state === 0 && data.paper.id && activityId.value)
      void files.load((data.paper.questions ?? []).flatMap(question => [question.body ?? '', ...(question.options ?? []).map(option => option.content ?? '')]), { activityId: activityId.value, paperId: data.paper.id })
  }
  async function load(initial = false) {
    if (readPending || disposed) return
    readPending = true; if (initial) loading.value = true
    const version = epoch, write = mutation
    try {
      if (!activityId.value && competitionId.value) {
        const link = await getCompetitionTheory({ path: { competitionId: competitionId.value }, query: { staff: false } })
        if (link.error) throw link.error
        activityId.value = link.data?.[0]?.id ?? ''
      }
      if (!activityId.value) return
      const response = await getTheorySession({ path: { activityId: activityId.value } })
      if (response.error || !response.data) throw response.error
      if (version === epoch && write === mutation && !disposed) { receive(response.data); error.value = null }
    }
    catch (cause) {
      if (version === epoch && !disposed) {
        const failure = parseApiError(cause, message('theory.loadFailed')); error.value = failure.displayMessage
        if (failure.status === 401 || failure.status === 403 || failure.status === 404) { view.value = null; files.clear(); Object.keys(drafts).forEach(id => delete drafts[id]) }
      }
    }
    finally { readPending = false; if (version === epoch) loading.value = false }
  }
  function choose(ids: string[]) {
    if (!canAnswer.value || !selected.value?.id || !draft.value || remaining.value === 0) return
    selectAnswer(draft.value, ids); queue.add(selected.value.id)
    clearTimeout(saveTimer); saveTimer = setTimeout(() => void drain(), 250)
  }
  function chooseSingle(value: unknown) { if (typeof value === 'string') choose([value]) }
  function chooseMultiple(id: string | undefined, checked: unknown) {
    if (!id || !draft.value) return
    choose(checked === true ? [...draft.value.selected, id] : draft.value.selected.filter(value => value !== id))
  }
  async function drain() {
    if (draining) return draining
    draining = (async () => {
      while (queue.size && !disposed && view.value?.canAnswer) {
        const itemId = queue.values().next().value as string; queue.delete(itemId)
        const local = drafts[itemId], paperId = view.value?.paper?.id
        if (!local || !paperId || !activityId.value) continue
        const request = prepareAnswer(local, crypto.randomUUID())
        if (!request) continue
        mutation++
        try {
          const response = await answerTheoryPaper({ path: { activityId: activityId.value, paperId, itemId }, body: {
            expectedRevision: request.revision, requestId: request.requestId, optionIds: request.selected } })
          if (response.error || !response.data) throw response.error
          if (disposed || view.value?.paper?.id !== paperId) return
          const server = response.data.paper?.questions?.find(question => question.id === itemId)
          if (server) acknowledgeAnswer(local, request, saved(server))
          receive(response.data); error.value = null
          if (local.dirty && !local.conflict && response.data.paper?.state === 0) queue.add(itemId)
        }
        catch (cause) {
          if (disposed) return
          local.saving = false
          const failure = parseApiError(cause, message('theory.saveFailed')); error.value = failure.displayMessage
          if (failure.code === 'AnswerConflict') { local.conflict = true; local.pending = null; await load() }
          queue.clear(); break
        }
        finally { mutation++ }
      }
    })().finally(() => { draining = null })
    return draining
  }
  async function start() {
    if (!activityId.value || busy.value || !view.value?.canIssue) return
    busy.value = true; mutation++; issueRequest ||= crypto.randomUUID()
    try {
      const response = await issueTheoryPaper({ path: { activityId: activityId.value }, body: { requestId: issueRequest, expectedIssuedCount: view.value.issuedCount ?? 0 } })
      if (response.error || !response.data) throw response.error
      receive(response.data); issueRequest = ''; error.value = null
    }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { mutation++; busy.value = false }
  }
  async function requestSubmit() {
    if (!view.value?.canAnswer || busy.value) return
    busy.value = true; clearTimeout(saveTimer)
    try {
      Object.entries(drafts).forEach(([id, draft]) => { if (draft.dirty || draft.pending) queue.add(id) })
      await drain()
      if (unsaved.value) { error.value = message('theory.unsynced'); return }
      await load()
      if (!view.value?.canAnswer || !view.value.paper?.id || !view.value.paper.answerRevision) return
      confirmation.value = { paperId: view.value.paper.id, revision: view.value.paper.answerRevision }; submitOpen.value = true
    }
    finally { busy.value = false }
  }
  async function submit() {
    const target = confirmation.value
    if (!target || busy.value || !activityId.value) return
    busy.value = true; mutation++
    try {
      const response = await submitTheoryPaper({ path: { activityId: activityId.value, paperId: target.paperId }, body: { expectedRevision: target.revision } })
      if (response.error || !response.data) throw response.error
      receive(response.data); error.value = null; toast.success(message('theory.submitted'))
    }
    catch (cause) { const failure = parseApiError(cause); error.value = failure.displayMessage; if (failure.code === 'PaperConflict') { submitOpen.value = false; confirmation.value = null } }
    finally { mutation++; busy.value = false; void load() }
  }
  function setSubmitOpen(value: boolean) { if (!busy.value) submitOpen.value = value }
  function go(index: number) { current.value = Math.max(0, Math.min(index, questions.value.length - 1)); sheetOpen.value = false; void router.replace({ query: { ...route.query, q: current.value + 1 } }) }
  function mark() { const id = selected.value?.id; if (id) { if (marks.has(id)) marks.delete(id); else marks.add(id) } }
  function useRemote() { if (draft.value) acceptRemoteAnswer(draft.value) }
  function keepLocal() { if (!draft.value || !selected.value?.id) return; overwriteRemoteAnswer(draft.value); queue.add(selected.value.id); void drain() }
  function retrySave() { Object.entries(drafts).forEach(([id, draft]) => { if (draft.dirty || draft.pending) queue.add(id) }); void drain() }
  async function loadBoard() {
    if (!activityId.value || boardLoading.value) return
    boardLoading.value = true
    try {
      const response = await getTheoryExamLeaderboard({ path: { activityId: activityId.value }, query: { offset: (boardPage.value - 1) * boardLimit.value, limit: boardLimit.value, desc: true } })
      if (response.error) throw response.error
      board.value = response.data ?? null
    }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { boardLoading.value = false }
  }
  function openBoard() { boardOpen.value = true; void loadBoard() }
  function setBoardPage(value: number) { boardPage.value = value; void loadBoard() }
  function setBoardLimit(value: number) { boardLimit.value = value; setBoardPage(1) }
  function confirmLeave() { if (!unsaved.value) return Promise.resolve(true); leaveOpen.value = true; return new Promise<boolean>(resolve => { leaveResolver = resolve }) }
  function stay() { leaveOpen.value = false; leaveResolver?.(false); leaveResolver = undefined }
  function discard() { leaveOpen.value = false; leaveResolver?.(true); leaveResolver = undefined }
  function setLeaveOpen(value: boolean) { if (!value) stay() }
  function beforeUnload(event: BeforeUnloadEvent) { if (unsaved.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(confirmLeave); onBeforeRouteUpdate((to, from) => to.path === from.path ? true : confirmLeave())
  onMounted(() => {
    void load(true); window.addEventListener('beforeunload', beforeUnload)
    unwatch = watchNotifications({ theoryChanged: id => { if (id === activityId.value && !busy.value && !draining) void load() }, onReconnected: () => void load() })
    interval = setInterval(() => { clock.value = Date.now(); tickCount++; if ((tickCount % 15 === 0 || remaining.value === 0 && view.value?.paper?.state === 0) && !busy.value && !draining && !submitOpen.value) void load() }, 1000)
  })
  onBeforeUnmount(() => { disposed = true; epoch++; unwatch?.(); clearInterval(interval); clearTimeout(saveTimer); window.removeEventListener('beforeunload', beforeUnload); leaveResolver?.(false) })
  return { view, loading, busy, error, questions, answerItems, current, selected, draft, drafts, marks, sheetOpen, canAnswer, unsaved, answered, remainingLabel, remaining,
    assets: files.assets, load, start, chooseSingle, chooseMultiple, clearAnswer: () => choose([]), go, selectNumber: (value: string) => go(Number(value)), mark, useRemote, keepLocal, retrySave,
    boardPage, boardLimit, boardLoading, setBoardPage, setBoardLimit, lostUnsynced, boardPageCount: computed(() => Math.max(1, Math.ceil((board.value?.total ?? 0) / boardLimit.value))),
    submitOpen, setSubmitOpen, requestSubmit, submit, board, boardOpen, openBoard, leaveOpen, stay, discard, setLeaveOpen, questionKindLabel, scoringLabel,
    openSheet: () => { sheetOpen.value = true }, previous: () => go(current.value - 1), next: () => go(current.value + 1),
    statusKey: computed(() => view.value?.deadline?.paused ? 'theory.paused' : remaining.value === 0 ? 'theory.closed' : 'theory.notOpen'),
    formatTime: (value?: string | null) => value ? new Date(value).toLocaleString(localeTag()) : '',
    back: () => router.push(competitionId.value ? `/competitions/${competitionId.value}/challenges` : '/theory'),
    optionLabel: (index: number) => String.fromCharCode(65 + index) }
}
export type TheoryAnswerPageState = import('vue').ShallowUnwrapRef<ReturnType<typeof useTheoryAnswerPage>>
