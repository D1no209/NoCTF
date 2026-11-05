import { getQuestionnaireActivityManagement, saveQuestionnaireActivity, transitionQuestionnaireActivity, adminChallengeBankListTemplates, adminChallengeBankGetTemplate,
  listQuestionnaireReviews, getQuestionnaireReview, previewQuestionnaireCorrection, applyQuestionnaireCorrection, voidQuestionnaireSession, checkQuestionnaireActivity, listQuestionnaireAudit } from '~/api'
import type { NoCtfApplicationQuestionnairesActivitiesQuestionnaireActivityInput as ActivityInput, NoCtfApplicationQuestionnairesActivitiesQuestionnaireActivityManagement as Management,
  NoCtfApplicationQuestionnairesActivitiesQuestionnaireReviewRow as ReviewRow, GetQuestionnaireReviewResponse as ReviewSession,
  NoCtfApplicationQuestionnairesActivitiesQuestionnaireCorrectionPreview as CorrectionPreview, NoCtfApplicationQuestionnairesActivitiesQuestionnaireActivityAction as Action,
  NoCtfApplicationQuestionnairesActivitiesQuestionnaireCorrectionInput as CorrectionInput, NoCtfApplicationQuestionnairesActivitiesQuestionnaireActivityCheck as PoolCheck, NoCtfApplicationQuestionnairesActivitiesQuestionnaireAuditPage as AuditPage } from '~/api'
import { parseApiError } from '~/utils/api-error'
import { message, localeTag, type UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import { localDateTime, questionKindLabel, activityStateLabel, scoringLabel } from './questionnaire-labels'
import { useQuestionnaireAssets } from './useQuestionnaireAssets'
import { watchNotifications } from '~/composables/useNotificationHub'

type Rule = { key: string; kind: string; difficulty: string; count: number | string; points: number | string; knowledge: string; tags: string }
const newRule = (): Rule => ({ key: crypto.randomUUID(), kind: '0', difficulty: 'any', count: 1, points: 10, knowledge: '', tags: '' })
const newFixedQuestionnaire = () => ({ key: crypto.randomUUID(), challengeId: '', pointsPerQuestion: 10 as number | string })

export function useQuestionnaireManagementPage() {
  const route = useRoute(), router = useRouter(), { canOrganize } = useAuth()
  const activityId = ref(typeof route.params.activityId === 'string' ? route.params.activityId : '')
  const management = ref<Management | null>(null), loading = ref(false), busy = ref(false), error = ref<UiMessage | null>(null)
  const now = new Date(), tomorrow = new Date(now.getTime() + 86400000)
  const form = reactive({ name: '', kind: '0', opensAt: localDateTime(now.toISOString()), closesAt: localDateTime(tomorrow.toISOString()),
    duration: 30 as number | string, attempts: 1 as number | string, unlimited: false, scoring: '0', allowExposed: false, rules: [newRule()],
    selectionKind: '0', fixedQuestionnaires: [newFixedQuestionnaire()] })
  const questionnaireOptions = ref<{ value: string; label: string }[]>([]), questionnaireCounts = ref<Record<string, number>>({})
  const baseline = ref(''), dirty = computed(() => baseline.value !== '' && baseline.value !== JSON.stringify(form))
  const canManage = computed(() => management.value?.canManage ?? canOrganize.value)
  const editable = computed(() => canManage.value && (!management.value || management.value.activity?.state === 0))
  const tab = ref(typeof route.query.tab === 'string' ? route.query.tab : 'settings')
  const poolCheck = ref<PoolCheck | null>(null)
  const rows = ref<ReviewRow[]>([]), page = ref(1), limit = ref(20), total = ref(0), review = ref<ReviewSession | null>(null)
  const selectedQuestionId = ref(''), correctionMode = ref('0'), correctOptions = ref<string[]>([]), correctionPreview = ref<CorrectionPreview | null>(null)
  const correctionReason = ref(''), pendingTarget = ref(''), correctionNotice = ref<UiMessage | null>(null), files = useQuestionnaireAssets()
  const selectedQuestion = computed(() => review.value?.questions?.find(item => item.question?.id === selectedQuestionId.value) ?? null)
  const actionOpen = ref(false), action = ref<Action>(0), actionReason = ref(''), extension = ref('')
  const sessionVoidOpen = ref(false), audit = ref<AuditPage | null>(null), auditPage = ref(1), auditLimit = ref(20)
  const leaveOpen = ref(false); let leaveResolver: ((allow: boolean) => void) | undefined
  let epoch = 0, reviewEpoch = 0, rowsEpoch = 0
  let metadataEpoch = 0
  let unwatch: (() => void) | undefined
  const correctionPoll = usePolling(async () => {
    if (!activityId.value) return true
    const response = await getQuestionnaireActivityManagement({ path: { activityId: activityId.value } })
    if (response.error || !response.data) throw response.error
    management.value = response.data
    const status = response.data.corrections?.find(item => item.targetRevision === pendingTarget.value)
    if (!status || status.completed) {
      correctionNotice.value = message(status ? 'theory.correctionDone' : 'theory.correctionSuperseded'); await loadRows()
      const sessionId = review.value?.session?.sessionId
      if (sessionId) {
        const current = await getQuestionnaireReview({ path: { activityId: activityId.value, sessionId } })
        if (current.data && review.value?.session?.sessionId === sessionId) review.value = current.data
      }
      return true
    }
    return false
  }, { timeout: 180000, maxInterval: 5000 })

  function fill(data: Management) {
    const value = data.activity
    if (!value) return
    form.name = value.name ?? ''; form.kind = String(value.kind ?? 0)
    form.opensAt = localDateTime(value.opensAt ?? now.toISOString()); form.closesAt = localDateTime(value.closesAt ?? tomorrow.toISOString())
    form.duration = (value.durationSeconds ?? 1800) / 60; form.attempts = value.attemptLimit ?? 1
    form.unlimited = value.attemptLimit == null; form.scoring = String(value.scoringMode ?? 0); form.allowExposed = data.allowExposedQuestions ?? false
    form.selectionKind = String(data.selectionKind ?? 0)
    form.fixedQuestionnaires = data.fixedQuestionnaires?.length ? data.fixedQuestionnaires.map(value => ({ key: crypto.randomUUID(),
      challengeId: value.challengeId ?? '', pointsPerQuestion: value.pointsPerQuestion ?? 10 })) : [newFixedQuestionnaire()]
    form.rules = (data.rules ?? []).map(rule => ({ key: crypto.randomUUID(), kind: String(rule.kind ?? 0), difficulty: rule.difficulty == null ? 'any' : String(rule.difficulty),
      count: rule.count ?? 1, points: rule.points ?? 10, knowledge: (rule.filters ?? []).filter(item => item.kind === 0).map(item => item.name).join(', '),
      tags: (rule.filters ?? []).filter(item => item.kind === 1).map(item => item.name).join(', ') }))
    baseline.value = JSON.stringify(form)
  }
  async function load() {
    const current = ++epoch; loading.value = true; error.value = null
    try {
      if (canOrganize.value) await loadQuestionnaireCatalog(current)
      if (current !== epoch) return
      if (!activityId.value) { baseline.value = JSON.stringify(form); return }
      const response = await getQuestionnaireActivityManagement({ path: { activityId: activityId.value } })
      if (response.error || !response.data) throw response.error
      if (current !== epoch) return
      management.value = response.data; fill(response.data)
      if (tab.value === 'monitor') await loadRows(); if (tab.value === 'audit') await loadAudit()
    }
    catch (cause) { if (current === epoch) error.value = parseApiError(cause, message('theory.loadFailed')).displayMessage }
    finally { if (current === epoch) loading.value = false }
  }
  function filters(value: string, kind: 0 | 1) { return value.split(/[,，]/).map(name => name.trim()).filter(Boolean).map(name => ({ kind, name })) }
  async function loadQuestionnaireCatalog(current: number) {
    const options: { value: string; label: string }[] = []
    for (let offset = 0; ; offset += 200) {
      const response = await adminChallengeBankListTemplates({ query: { requiredCapabilities: ['Questionnaire'], includeDeleted: false, onlyMine: false, desc: false, offset, limit: 200 } })
      if (current !== epoch) return
      if (response.error || !response.data) throw response.error
      for (const row of response.data.items ?? []) if (row.id) options.push({ value: row.id, label: row.title ?? row.id })
      if ((response.data.items?.length ?? 0) < 200 || options.length >= (response.data.total ?? options.length)) break
    }
    questionnaireOptions.value = options
  }
  async function loadQuestionnaireCounts() {
    const current = ++metadataEpoch
    if (form.selectionKind !== '1' || !editable.value) return
    const ids = [...new Set(form.fixedQuestionnaires.map(value => value.challengeId).filter(Boolean))]
    await Promise.all(ids.map(async challengeId => {
      const response = await adminChallengeBankGetTemplate({ path: { challengeId }, query: { includeDeleted: false } })
      if (current !== metadataEpoch) return
      if (response.error || !response.data) { error.value = parseApiError(response.error, message('theory.loadFailed')).displayMessage; return }
      questionnaireCounts.value[challengeId] = response.data.definition?.capabilities?.find(value => value.kind === 'Questionnaire')?.questionnaire?.questions?.length ?? 0
    }))
  }
  function payload(): ActivityInput {
    return { name: form.name.trim(), kind: Number(form.kind) as 0 | 1, opensAt: new Date(form.opensAt).toISOString(), closesAt: new Date(form.closesAt).toISOString(),
      durationSeconds: Math.round(Number(form.duration) * 60), attemptLimit: form.unlimited ? null : Number(form.attempts), scoringMode: Number(form.scoring) as 0 | 1 | 2,
      selectionKind: Number(form.selectionKind) as 0 | 1,
      fixedQuestionnaires: form.selectionKind === '1' ? form.fixedQuestionnaires.map(value => ({ challengeId: value.challengeId, pointsPerQuestion: Number(value.pointsPerQuestion) })) : [],
      allowExposedQuestions: form.allowExposed, rules: form.selectionKind === '1' ? [] : form.rules.map(rule => ({ kind: Number(rule.kind) as 0 | 1 | 2,
        difficulty: rule.difficulty === 'any' ? null : Number(rule.difficulty) as 0 | 1 | 2,
        count: Number(rule.count), points: Number(rule.points), filters: [...filters(rule.knowledge, 0), ...filters(rule.tags, 1)] })) }
  }
  async function save() {
    if (busy.value || !editable.value) return
    busy.value = true; error.value = null
    try {
      const input = payload()
      const response = await saveQuestionnaireActivity({ body: { activityId: activityId.value || null, input } })
      if (response.error || !response.data?.id) throw response.error
      baseline.value = JSON.stringify(form); activityId.value = response.data.id; poolCheck.value = null
      if (!route.params.activityId) await router.replace(`/admin/questionnaires/activities/${activityId.value}`)
      await load(); toast.success(message('theory.saved'))
    }
    catch (cause) { error.value = parseApiError(cause, message('theory.saveFailed')).displayMessage }
    finally { busy.value = false }
  }
  async function check() {
    if (!activityId.value || dirty.value || busy.value) return false
    busy.value = true; error.value = null
    try {
      const response = await checkQuestionnaireActivity({ path: { activityId: activityId.value } })
      if (response.error || !response.data) throw response.error
      poolCheck.value = response.data
      if (!response.data.feasible) error.value = message('api.theory.theoryActivityError.insufficientQuestions')
      return response.data.feasible === true
    }
    catch (cause) { error.value = parseApiError(cause).displayMessage; return false }
    finally { busy.value = false }
  }
  async function requestAction(value: Action) {
    if (busy.value || !canManage.value || !activityId.value || dirty.value) return
    if (value === 0 && !await check()) return
    action.value = value; actionReason.value = ''; extension.value = localDateTime(new Date(new Date(management.value?.activity?.closesAt ?? form.closesAt).getTime() + 3600000).toISOString())
    actionOpen.value = true
  }
  async function performAction() {
    if (busy.value || !activityId.value) return
    busy.value = true
    try {
      const response = await transitionQuestionnaireActivity({ path: { activityId: activityId.value }, body: { action: action.value,
        deadline: action.value === 5 ? new Date(extension.value).toISOString() : null, reason: actionReason.value || null } })
      if (response.error) throw response.error
      actionOpen.value = false; await load(); toast.success(message('theory.saved'))
    }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { busy.value = false }
  }
  function setActionOpen(value: boolean) { if (!busy.value) actionOpen.value = value }
  async function loadRows() {
    if (!activityId.value) return
    const current = ++rowsEpoch
    const response = await listQuestionnaireReviews({ path: { activityId: activityId.value }, query: { offset: (page.value - 1) * limit.value, limit: limit.value, desc: true } })
    if (current !== rowsEpoch) return
    if (response.error) { error.value = parseApiError(response.error).displayMessage; return }
    rows.value = response.data?.items ?? []; total.value = response.data?.total ?? 0
  }
  async function openSession(row: ReviewRow) {
    if (!activityId.value || !row.sessionId) return
    const current = ++reviewEpoch
    const response = await getQuestionnaireReview({ path: { activityId: activityId.value, sessionId: row.sessionId } })
    if (current !== reviewEpoch) return
    if (response.error || !response.data) { error.value = parseApiError(response.error).displayMessage; return }
    review.value = response.data; selectQuestion(response.data.questions?.[0]?.question?.id ?? '')
    void files.load((response.data.questions ?? []).flatMap(item => [item.question?.body ?? '', item.explanation ?? '', ...(item.question?.options ?? []).map(option => option.content ?? '')]),
      { activityId: activityId.value, sessionId: row.sessionId, staff: true })
  }
  function selectQuestion(id: string) { selectedQuestionId.value = id; correctionMode.value = '0'; correctOptions.value = [...(selectedQuestion.value?.correctOptions ?? [])]; correctionPreview.value = null; correctionReason.value = '' }
  function setCorrectionOption(id: string | undefined, selected: unknown) { if (!id) return; correctOptions.value = selected === true ? [...new Set([...correctOptions.value, id])] : correctOptions.value.filter(value => value !== id); correctionPreview.value = null }
  function correctionInput(): CorrectionInput { return { revisionId: selectedQuestion.value?.revisionId ?? '', mode: Number(correctionMode.value) as 0 | 1,
    correctOptions: correctionMode.value === '1' ? [] : correctOptions.value } }
  async function previewCorrection() {
    if (!activityId.value || busy.value) return
    busy.value = true
    try { const response = await previewQuestionnaireCorrection({ path: { activityId: activityId.value }, body: { input: correctionInput() } }); if (response.error || !response.data) throw response.error; correctionPreview.value = response.data }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { busy.value = false }
  }
  async function correct() {
    if (!activityId.value || !correctionPreview.value?.token || !correctionPreview.value.expiresAt || busy.value) return
    busy.value = true
    try {
      const response = await applyQuestionnaireCorrection({ path: { activityId: activityId.value }, body: { input: correctionInput(), token: correctionPreview.value.token,
        expiresAt: correctionPreview.value.expiresAt, reason: correctionReason.value } })
      if (response.error || !response.data?.targetRevision) throw response.error
      pendingTarget.value = response.data.targetRevision; correctionNotice.value = message('theory.correctionPending'); correctionPreview.value = null; correctionPoll.start()
    }
    catch (cause) { error.value = parseApiError(cause).displayMessage; correctionPreview.value = null }
    finally { busy.value = false }
  }
  async function voidSession() {
    if (!activityId.value || !review.value?.session?.sessionId || busy.value || !correctionReason.value.trim()) return
    busy.value = true
    try { const response = await voidQuestionnaireSession({ path: { activityId: activityId.value, sessionId: review.value.session.sessionId }, body: { reason: correctionReason.value } }); if (response.error) throw response.error; sessionVoidOpen.value = false; review.value = null; await loadRows() }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { busy.value = false }
  }
  async function loadAudit() {
    if (!activityId.value) return
    const response = await listQuestionnaireAudit({ path: { activityId: activityId.value }, query: { offset: (auditPage.value - 1) * auditLimit.value, limit: auditLimit.value, desc: true } })
    if (response.error) error.value = parseApiError(response.error).displayMessage
    else audit.value = response.data ?? null
  }
  function setAuditPage(value: number) { auditPage.value = value; void loadAudit() }
  function setAuditLimit(value: number) { auditLimit.value = value; setAuditPage(1) }
  function setPage(value: number) { page.value = value; void loadRows() }
  function requestVoidSession() { correctionReason.value = ''; sessionVoidOpen.value = true }
  function setSessionVoidOpen(value: boolean) { if (!busy.value) sessionVoidOpen.value = value }
  function setLimit(value: number) { limit.value = value; setPage(1) }
  function addRule() { form.rules.push(newRule()) }
  function addFixedQuestionnaire() { form.fixedQuestionnaires.push(newFixedQuestionnaire()) }
  function removeFixedQuestionnaire(key: string) { if (form.fixedQuestionnaires.length > 1) form.fixedQuestionnaires = form.fixedQuestionnaires.filter(value => value.key !== key) }
  function removeRule(key: string) { if (form.rules.length > 1) form.rules = form.rules.filter(rule => rule.key !== key) }
  function confirmLeave() { if (!dirty.value) return Promise.resolve(true); leaveOpen.value = true; return new Promise<boolean>(resolve => { leaveResolver = resolve }) }
  function stay() { leaveOpen.value = false; leaveResolver?.(false); leaveResolver = undefined }
  function discard() { baseline.value = JSON.stringify(form); leaveOpen.value = false; leaveResolver?.(true); leaveResolver = undefined }
  function setLeaveOpen(value: boolean) { if (!value) stay() }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  watch(tab, () => { void router.replace({ query: { ...route.query, tab: tab.value } }); if (tab.value === 'monitor') void loadRows(); if (tab.value === 'audit') void loadAudit() })
  watch(correctionMode, () => { correctionPreview.value = null })
  watch(() => form.kind, () => { if (form.kind !== '0') form.unlimited = false })
  watch(() => form.scoring, () => { if (form.scoring === '2') form.unlimited = false })
  watch(() => `${form.selectionKind}:${form.fixedQuestionnaires.map(value => value.challengeId).join(',')}`, () => { void loadQuestionnaireCounts() })
  onBeforeRouteLeave(confirmLeave); onBeforeRouteUpdate((to, from) => to.path === from.path ? true : confirmLeave())
  onMounted(() => { void load(); window.addEventListener('beforeunload', beforeUnload)
    unwatch = watchNotifications({ questionnaireChanged: id => { if (id === activityId.value && tab.value === 'monitor') void loadRows() }, onReconnected: () => { if (tab.value === 'monitor') void loadRows() } }) })
  onBeforeUnmount(() => { epoch++; metadataEpoch++; reviewEpoch++; rowsEpoch++; unwatch?.(); correctionPoll.stop(); window.removeEventListener('beforeunload', beforeUnload); leaveResolver?.(false) })
  return { audit, auditPage, auditLimit, loadAudit, setAuditPage, setAuditLimit, auditPageCount: computed(() => Math.max(1, Math.ceil((audit.value?.total ?? 0) / auditLimit.value))),
    operationLabel: (value: number) => (['theory.published', 'theory.withdraw', 'theory.pause', 'theory.resume', 'theory.extend', 'theory.stopIssuance', 'theory.finish', 'theory.void', 'theory.allowExposed', 'theory.void', 'theory.replaceAnswer', 'theory.fullCredit'] as const)[value] ?? 'theory.audit',
    activityId, management, loading, busy, error, form, dirty, canManage, editable, tab, poolCheck, save, check, load, requestAction, performAction, actionOpen, action, actionReason, extension, setActionOpen,
    rows, page, limit, total, pageCount: computed(() => Math.max(1, Math.ceil(total.value / limit.value))), setPage, setLimit, loadRows, openSession,
    review, selectedQuestion, selectedQuestionId, selectQuestion, correctionMode, correctOptions, setCorrectionOption, correctionReason, correctionPreview, previewCorrection, correct,
    correctionNotice, correctionPending: correctionPoll.polling, voidSession, requestVoidSession, sessionVoidOpen, setSessionVoidOpen, assets: files.assets, addRule, removeRule, leaveOpen, stay, discard, setLeaveOpen,
    questionnaireOptions, addFixedQuestionnaire, removeFixedQuestionnaire,
    maximum: computed(() => form.selectionKind === '1' ? !editable.value ? management.value?.activity?.maximumSessionPoints ?? 0
      : form.fixedQuestionnaires.reduce((sum, value) => sum + (questionnaireCounts.value[value.challengeId] ?? 0) * Number(value.pointsPerQuestion), 0)
      : form.rules.reduce((sum, rule) => sum + Number(rule.count) * Number(rule.points), 0)),
    questionKindLabel, activityStateLabel, scoringLabel,
    formatTime: (value?: string) => value ? new Date(value).toLocaleString(localeTag()) : '',
    actionLabel: (value: Action) => (['theory.publish', 'theory.withdraw', 'theory.pause', 'theory.resume', 'theory.stopIssuance', 'theory.extend', 'theory.finish', 'theory.void'] as const)[value],
    back: () => router.push('/admin/questionnaires/activities') }
}
export type QuestionnaireManagementState = import('vue').ShallowUnwrapRef<ReturnType<typeof useQuestionnaireManagementPage>>
