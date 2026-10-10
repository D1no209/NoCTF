import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { adminChallengeBankListTemplates, adminListCompetitionChallenges, copyLiveSoloTemplate, getLiveSoloConfiguration,
  listLiveSoloQuestionGroups, saveLiveSoloQuestionGroup } from '~/api'
import type { NoCtfapiEndpointsChallengesChallengeSummaryResponse as Challenge, NoCtfapiEndpointsLiveSoloLiveSoloQuestionGroupResponse as Group,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse as Template, NoCtfapiEndpointsLiveSoloLiveSoloTemplateCopyResponse as Copy } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { ApiError } from '~/utils/api-error'
import { canManageLiveSolo } from './settings-draft'
import { groupDraft, validGroup, groupOffsets, moveGroupQuestion, copyCandidates } from './group-draft'

export function useLiveSoloGroups() {
  const route = useRoute(), router = useRouter(), context = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), groupId = computed(() => typeof route.params.groupId === 'string' ? route.params.groupId : null)
  const writable = computed(() => canManageLiveSolo(context?.competition.value?.administrationRole))
  const groups = ref<Group[]>([]), challenges = ref<Challenge[]>([]), interval = ref(180), limit = ref(900)
  const draft = ref(groupDraft()), original = ref(''), loading = ref(true), busy = ref(false), error = ref<UiMessage | null>(null), saved = ref(false)
  const selectedQuestion = ref<string | null>(null), copyId = ref<string | null>(null), copyAttachments = ref(true), copyFlags = ref(false)
  const copyDialog = ref(false), copyTarget = ref<string | null>(null), keyword = ref('')
  const copied = ref<Copy | null>(null)
  const leaveOpen = ref(false)
  let disposed = false, request = 0, allowLeave = false, leaveTo: string | null = null, reloadPending = false, newPending = false
  const dirty = computed(() => original.value !== '' && JSON.stringify(draft.value) !== original.value)
  const options = computed(() => groups.value.filter(row => row.id).map(row => ({ value: row.id!, label: row.name ?? '—', row })))
  const questionOptions = computed(() => challenges.value.filter(row => row.id && !draft.value.questions.some(item => item.competitionChallengeId === row.id))
    .map(row => ({ value: row.id!, label: row.title ?? '—' })))
  const offsets = computed(() => groupOffsets(draft.value, interval.value))
  const questions = computed(() => draft.value.questions.map((row,index) => ({ ...row, index, effectiveOffset: offsets.value[index]!,
    title: challenges.value.find(question => question.id === row.competitionChallengeId)?.title ?? '—' })))
  const templates = useOffsetPagination<Template>(async ({ offset, limit }) => {
    const result = await adminChallengeBankListTemplates({ query: { offset, limit, desc: false, keyword: keyword.value || undefined, includeDeleted: false, onlyMine: false } })
    if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
    return result.data
  }, { initialPageSize: 20 })
  const templateOptions = computed(() => copyCandidates(templates.items.value).map(row => ({ value: row.id!, label: row.title ?? '—' })))
  const copyName = computed(() => templates.items.value.find(row => row.id === copyTarget.value)?.title ?? '—')
  async function load() {
    if (busy.value || disposed) return
    const generation = ++request; loading.value = true
    try {
      if (!context?.competition.value) await context?.refresh()
      const path = { competitionId: competitionId.value }
      const [pool, entries, settings] = await Promise.all([listLiveSoloQuestionGroups({ path }), adminListCompetitionChallenges({ path, query: { includeDeleted: false } }), getLiveSoloConfiguration({ path })])
      if (disposed || generation !== request) return
      if (pool.error || entries.error || settings.error || !pool.data || !entries.data || !settings.data) throw parseLiveSoloError(pool.error ?? entries.error ?? settings.error, message('liveSolo.error.load'))
      groups.value = pool.data.items ?? []; challenges.value = entries.data.items ?? []
      interval.value = settings.data.questionIntervalSeconds ?? 180; limit.value = settings.data.roundLimitSeconds ?? 900
      if (!dirty.value) {
        const selected = groupId.value ? groups.value.find(row => row.id === groupId.value) : undefined
        if (groupId.value && !selected) throw new ApiError(message('liveSolo.error.notFound'))
        draft.value = groupDraft(selected); original.value = JSON.stringify(draft.value)
      }
      error.value = null
    }
    catch (cause) { if (!disposed && generation === request) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { if (generation === request) loading.value = false }
  }
  async function select(id: string) { await router.push(`/competitions/${competitionId.value}/live-solo/groups/${id}`) }
  async function create() {
    if (groupId.value) { await router.push(`/competitions/${competitionId.value}/live-solo/groups`); return }
    if (dirty.value) { newPending = true; reloadPending = false; leaveOpen.value = true; return }
    draft.value = groupDraft(); original.value = JSON.stringify(draft.value)
  }
  function add() { if (selectedQuestion.value && writable.value && !busy.value && questionOptions.value.some(row => row.value === selectedQuestion.value)) { draft.value.questions.push({ competitionChallengeId: selectedQuestion.value, openOffsetSeconds: null }); selectedQuestion.value = null } }
  function remove(index: number) { if (writable.value && !busy.value) draft.value.questions.splice(index,1) }
  function move(index: number, direction: -1 | 1) { if (writable.value && !busy.value) draft.value.questions = moveGroupQuestion(draft.value,index,direction) }
  function offset(index: number, value: number | string) { const row = draft.value.questions[index]; if (row && writable.value && !busy.value) row.openOffsetSeconds = value === '' ? null : Number(value) }
  function setLimit(value: number | string) { if (writable.value && !busy.value) draft.value.limitSeconds = value === '' ? null : Number(value) }
  function reserve(value: boolean | 'indeterminate') { if (writable.value && !busy.value) draft.value.reserve = value === true }
  async function save() {
    if (!writable.value || busy.value) return
    if (!validGroup(draft.value,interval.value,limit.value)) { error.value = message('liveSolo.groups.invalidSchedule'); return }
    busy.value = true
    try {
      const result = await saveLiveSoloQuestionGroup({ path: { competitionId: competitionId.value }, body: { ...draft.value, name: draft.value.name.trim() } })
      if (result.error || !result.data) {
        const problem = parseLiveSoloError(result.error,message('liveSolo.error.operation'))
        if (problem.code === 'Conflict') throw new ApiError(message('liveSolo.groups.conflict'), { status:409, code:problem.code })
        throw problem
      }
      if (disposed) return
      draft.value = groupDraft(result.data); original.value = JSON.stringify(draft.value); saved.value = true; error.value = null
      groups.value = [...groups.value.filter(row => row.id !== result.data!.id), result.data]
      if (result.data.id !== groupId.value && result.data.id) { allowLeave = true; await router.replace(`/competitions/${competitionId.value}/live-solo/groups/${result.data.id}`) }
    }
    catch (cause) { if (!disposed) error.value = parseLiveSoloError(cause,message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false }
  }
  async function searchTemplates() { templates.reset(); copyId.value = null; await templates.loadPage() }
  function openCopy() { if (writable.value && copyId.value && templateOptions.value.some(row => row.value === copyId.value)) { copyTarget.value = copyId.value; copyDialog.value = true } }
  function setCopyDialog(value: boolean) { if (!busy.value) copyDialog.value = value }
  async function confirmCopy() {
    if (!writable.value || !copyTarget.value || busy.value) return
    busy.value = true
    try {
      const result = await copyLiveSoloTemplate({ path: { competitionId: competitionId.value }, body: { sourceChallengeId: copyTarget.value, copyAttachments: copyAttachments.value, copyFlags: copyFlags.value } })
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      if (disposed) return
      copied.value = result.data; copyDialog.value = false; copyId.value = null; copyTarget.value = null; error.value = null
    }
    catch (cause) { if (!disposed) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false; if (!disposed && !copyDialog.value) await load() }
  }
  function setLeave(value: boolean) { if (!busy.value) { leaveOpen.value = value; if (!value) { reloadPending = false; newPending = false } } }
  async function reload() { if (busy.value) return; if (dirty.value) { reloadPending = true; newPending = false; leaveOpen.value = true } else await load() }
  async function leave() {
    if (busy.value) return
    leaveOpen.value = false
    if (newPending) { newPending = false; draft.value = groupDraft(); original.value = JSON.stringify(draft.value); return }
    if (reloadPending) { reloadPending = false; draft.value = groupDraft(); original.value = ''; await load(); return }
    if (leaveTo) { allowLeave = true; await router.push(leaveTo) }
  }
  async function back() { await router.push(`/competitions/${competitionId.value}/live-solo`) }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(to => { if (allowLeave) return true; if (busy.value) return false; if (!dirty.value) return true; reloadPending = false; newPending = false; leaveTo = to.fullPath; leaveOpen.value = true; return false })
  onMounted(() => { void load(); window.addEventListener('beforeunload',beforeUnload) })
  onScopeDispose(() => { disposed = true; request++; window.removeEventListener('beforeunload',beforeUnload) })
  return { options, groupId, draft, loading, busy, error, saved, dirty, writable, questionOptions, questions, selectedQuestion, interval, limit, reload,
    select, create, add, remove, move, offset, setLimit, reserve, save, back, leaveOpen, setLeave, leave,
    keyword, copyId, copyAttachments, copyFlags, copyDialog, setCopyDialog, copyName, copied, templateOptions,
    templatePage: templates.page, templatePageCount: templates.pageCount, templateLimit: templates.limit, templateTotal: templates.total,
    templateLoading: templates.loading, templateError: templates.error, searchTemplates, templatePageChange: templates.loadPage,
    templateLimitChange: templates.setPageSize, openCopy, confirmCopy }
}
export type LiveSoloGroupsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloGroups>>
