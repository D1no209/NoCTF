import { liveSoloBasePath } from './navigation'
import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { listLiveSoloQuestionPool, getLiveSoloConfiguration,
  listLiveSoloQuestionGroups, saveLiveSoloQuestionGroup } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloPoolQuestionResponse as Challenge, NoCtfapiEndpointsLiveSoloLiveSoloQuestionGroupResponse as Group } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { takeGroupDraft, keepGroupDraft } from './group-handoff'
import { ApiError } from '~/utils/api-error'
import { canManageLiveSolo } from './settings-draft'
import { groupDraft, groupValidationError, groupOffsets, moveGroupQuestion } from './group-draft'

export function useLiveSoloGroups() {
  const route = useRoute(), router = useRouter(), context = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), groupId = computed(() => typeof route.params.groupId === 'string' ? route.params.groupId : null)
  const writable = computed(() => canManageLiveSolo(context?.competition.value?.administrationRole))
  const groups = ref<Group[]>([]), challenges = ref<Challenge[]>([]), interval = ref(180), limit = ref(900)
  const draft = ref(groupDraft()), original = ref(''), loading = ref(true), busy = ref(false), error = ref<UiMessage | null>(null), saved = ref(false)
  const selectedQuestion = ref<string | null>(null)
  const validationShown = ref(false)
  const { user } = useAuth()
  const handoffKey = computed(() => `${user.value?.userId}:${competitionId.value}:${groupId.value ?? 'new'}`)
  const leaveOpen = ref(false)
  let disposed = false, request = 0, allowLeave = false, leaveTo: string | null = null, reloadPending = false, newPending = false
  const dirty = computed(() => original.value !== '' && JSON.stringify(draft.value) !== original.value)
  const options = computed(() => groups.value.filter(row => row.id).map(row => ({ value: row.id!, label: row.name ?? '—', row })))
  const questionOptions = computed(() => challenges.value.filter(row => row.id && !draft.value.questions.some(item => item.competitionChallengeId === row.id))
    .map(row => ({ value: row.id!, label: row.title ?? '—' })))
  const offsets = computed(() => groupOffsets(draft.value, interval.value))
  const validation = computed(() => groupValidationError(draft.value, interval.value, limit.value))
  const displayedError = computed(() => error.value ?? (validationShown.value ? validation.value?.message ?? null : null))
  const nameError = computed(() => validationShown.value && validation.value?.field === 'name' ? validation.value.message : null)
  const limitError = computed(() => validation.value?.field === 'limit' ? validation.value.message : null)
  const selectionError = computed(() => validationShown.value && validation.value?.field === 'questions' ? validation.value.message : null)
  const questions = computed(() => draft.value.questions.map((row,index) => ({ ...row, index, effectiveOffset: offsets.value[index]!,
    title: challenges.value.find(question => question.id === row.competitionChallengeId)?.title ?? '—',
    validationError: validation.value?.field === 'offset' && validation.value.index === index ? validation.value.message : null })))
  async function load() {
    if (busy.value || disposed) return
    const generation = ++request; loading.value = true
    try {
      if (!context?.competition.value) await context?.refresh()
      const path = { competitionId: competitionId.value }
      const [pool, entries, settings] = await Promise.all([listLiveSoloQuestionGroups({ path }), listLiveSoloQuestionPool({ path }), getLiveSoloConfiguration({ path })])
      if (disposed || generation !== request) return
      if (pool.error || entries.error || settings.error || !pool.data || !entries.data || !settings.data) throw parseLiveSoloError(pool.error ?? entries.error ?? settings.error, message('liveSolo.error.load'))
      groups.value = pool.data.items ?? []; challenges.value = entries.data.items ?? []
      interval.value = settings.data.questionIntervalSeconds ?? 180; limit.value = settings.data.roundLimitSeconds ?? 900
      if (!dirty.value) {
        const selected = groupId.value ? groups.value.find(row => row.id === groupId.value) : undefined
        if (groupId.value && !selected) throw new ApiError(message('liveSolo.error.notFound'))
        draft.value = groupDraft(selected); original.value = JSON.stringify(draft.value)
      }
      const added = typeof route.query.added === 'string' ? route.query.added : null
      if (added && questionOptions.value.some(x => x.value === added) && writable.value) { selectedQuestion.value = added; add(); await router.replace({ query: { ...route.query, added: undefined } }) }
      error.value = null
    }
    catch (cause) { if (!disposed && generation === request) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { if (generation === request) loading.value = false }
  }
  async function select(id: string) { await router.push(`${liveSoloBasePath(competitionId.value, route.path)}/groups/${id}`) }
  async function create() {
    if (groupId.value) { await router.push(`${liveSoloBasePath(competitionId.value, route.path)}/groups`); return }
    if (dirty.value) { newPending = true; reloadPending = false; leaveOpen.value = true; return }
    draft.value = groupDraft(); original.value = JSON.stringify(draft.value); validationShown.value = false
  }
  function add() { if (selectedQuestion.value && writable.value && !busy.value && questionOptions.value.some(row => row.value === selectedQuestion.value)) { draft.value.questions.push({ competitionChallengeId: selectedQuestion.value, openOffsetSeconds: null }); selectedQuestion.value = null } }
  function remove(index: number) { if (writable.value && !busy.value) draft.value.questions.splice(index,1) }
  function move(index: number, direction: -1 | 1) { if (writable.value && !busy.value) draft.value.questions = moveGroupQuestion(draft.value,index,direction) }
  function offset(index: number, value: number | string) { const row = draft.value.questions[index]; if (row && writable.value && !busy.value) row.openOffsetSeconds = value === '' ? null : Number(value) }
  function setLimit(value: number | string) { if (writable.value && !busy.value) draft.value.limitSeconds = value === '' ? null : Number(value) }
  function reserve(value: boolean | 'indeterminate') { if (writable.value && !busy.value) draft.value.reserve = value === true }
  async function save() {
    if (!writable.value || busy.value) return
    error.value = null
    validationShown.value = true
    if (validation.value) return
    busy.value = true
    try {
      const result = await saveLiveSoloQuestionGroup({ path: { competitionId: competitionId.value }, body: { ...draft.value, name: draft.value.name.trim() } })
      if (result.error || !result.data) {
        const problem = parseLiveSoloError(result.error,message('liveSolo.error.operation'))
        if (problem.code === 'Conflict') throw new ApiError(message('liveSolo.groups.conflict'), { status:409, code:problem.code })
        throw problem
      }
      if (disposed) return
      draft.value = groupDraft(result.data); original.value = JSON.stringify(draft.value); saved.value = true; error.value = null; validationShown.value = false
      groups.value = [...groups.value.filter(row => row.id !== result.data!.id), result.data]
      if (result.data.id !== groupId.value && result.data.id) { allowLeave = true; await router.replace(`${liveSoloBasePath(competitionId.value, route.path)}/groups/${result.data.id}`) }
    }
    catch (cause) { if (!disposed) error.value = parseLiveSoloError(cause,message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false }
  }
  async function pool() {
    if (busy.value) return
    keepGroupDraft(handoffKey.value, draft.value, original.value)
    allowLeave = true
    await router.push({ path: `${liveSoloBasePath(competitionId.value, route.path)}/question-pool`, query: { group: groupId.value ?? 'new' } })
  }
  function setLeave(value: boolean) { if (!busy.value) { leaveOpen.value = value; if (!value) { reloadPending = false; newPending = false } } }
  async function reload() { if (busy.value) return; if (dirty.value) { reloadPending = true; newPending = false; leaveOpen.value = true } else await load() }
  async function leave() {
    if (busy.value) return
    leaveOpen.value = false
    if (newPending) { newPending = false; draft.value = groupDraft(); original.value = JSON.stringify(draft.value); validationShown.value = false; return }
    if (reloadPending) { reloadPending = false; draft.value = groupDraft(); original.value = ''; validationShown.value = false; await load(); return }
    if (leaveTo) { allowLeave = true; await router.push(leaveTo) }
  }
  async function back() { await router.push(`${liveSoloBasePath(competitionId.value, route.path)}/groups`) }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(to => { if (allowLeave) return true; if (busy.value) return false; if (!dirty.value) return true; reloadPending = false; newPending = false; leaveTo = to.fullPath; leaveOpen.value = true; return false })
  onMounted(() => { const restored = takeGroupDraft(handoffKey.value); if (restored) { draft.value = restored.draft; original.value = restored.original } void load(); window.addEventListener('beforeunload',beforeUnload) })
  onScopeDispose(() => { disposed = true; request++; window.removeEventListener('beforeunload',beforeUnload) })
  return { options, groupId, draft, loading, busy, error: displayedError, saved, dirty, writable, questionOptions, questions, selectedQuestion, interval, limit, reload,
    nameError, limitError, selectionError,
    select, create, add, remove, move, offset, setLimit, reserve, save, back, leaveOpen, setLeave, leave,
    pool }
}
export type LiveSoloGroupsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloGroups>>
