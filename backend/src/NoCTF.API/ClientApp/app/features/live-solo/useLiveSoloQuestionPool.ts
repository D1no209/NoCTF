import { computed, inject, markRaw, onMounted, onScopeDispose, ref, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { listLiveSoloQuestionPool, createLiveSoloPoolQuestion, deployLiveSoloPoolQuestion, editLiveSoloPoolQuestion,
  exportLiveSoloPoolQuestion, uploadLiveSoloPoolAttachments, adminChallengeBankListTemplates } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloPoolQuestionResponse as Question,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeTemplateSummaryResponse as Template } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { canManageLiveSolo } from './settings-draft'
import { deploymentCandidates, poolDraft, poolDraftErrors } from './pool-draft'
import DefinitionEditor from '~/features/admin/DefinitionEditor.vue'

export function useLiveSoloQuestionPool() {
  const route = useRoute(), router = useRouter(), context = inject(competitionContextKey), { canOrganize, user } = useAuth()
  const competitionId = computed(() => route.params.id as string), writable = computed(() => canManageLiveSolo(context?.competition.value?.administrationRole))
  const questions = ref<Question[]>([]), selected = ref<string | null>(null), draft = ref(poolDraft()), original = ref('')
  const loading = ref(true), busy = ref(false), error = ref<UiMessage | null>(null), saved = ref(false)
  const uploadFiles = ref<File[]>([]), uploadKey = ref(0), deliveryPolicy = ref<'All' | 'RandomOnePerTeam'>('All'), downloadFileName = ref('')
  const deployOpen = ref(false), sourceId = ref<string | null>(null), keyword = ref(''), leaveOpen = ref(false)
  const current = computed(() => questions.value.find(x => x.id === selected.value) ?? null)
  const dirty = computed(() => original.value !== JSON.stringify(draft.value))
  const options = computed(() => questions.value.filter(x => x.id).map(x => ({ value: x.id!, label: x.title ?? '—', row: x })))
  const returning = computed(() => typeof route.query.group === 'string')
  const sources = useOffsetPagination<Template>(async ({ offset, limit }) => {
    const result = await adminChallengeBankListTemplates({ query: { includeDeleted: false, onlyMine: false, desc: false, offset, limit, mode: 'LiveSolo', keyword: keyword.value || undefined } })
    if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
    return result.data
  })
  const sourceOptions = computed(() => deploymentCandidates(sources.items.value).map(x => ({ value: x.id!, label: x.title ?? '—' })))
  let disposed = false, generation = 0, allowLeave = false, leaveTo: string | null = null, nextSelection: string | null | undefined
  const attachments = computed(() => current.value?.attachments?.filter(x => !draft.value.removedAttachmentIds.includes(x.id ?? '')) ?? [])
  function addFlag() { if (draft.value.flags && writable.value && !busy.value) draft.value.flags.push({ flag: '', matchKind: 'Exact' }) }
  function removeFlag(index: number) { if (writable.value && !busy.value) draft.value.flags?.splice(index, 1) }
  function removeAttachment(id: string) { if (writable.value && !busy.value) draft.value.removedAttachmentIds.push(id) }
  function chooseFiles(event: Event) { uploadFiles.value = Array.from((event.target as HTMLInputElement).files ?? []) }
  async function upload() {
    if (busy.value || dirty.value || !writable.value || !current.value?.id || !uploadFiles.value.length) return
    busy.value = true
    const actor = user.value?.userId, epoch = generation
    try {
      const result = await uploadLiveSoloPoolAttachments({ path: { competitionId: competitionId.value, competitionChallengeId: current.value.id }, body: { expectedStamp: current.value.concurrencyStamp!, expectedMaterialStamp: current.value.materialStamp!, deliveryPolicy: deliveryPolicy.value, downloadFileName: downloadFileName.value || null, files: uploadFiles.value } })
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      if (disposed || actor !== user.value?.userId || epoch !== generation) return
      questions.value = [...questions.value.filter(x => x.id !== result.data!.id), result.data]; apply(result.data.id ?? null); error.value = null
    } catch (cause) { if (!disposed && epoch === generation && actor === user.value?.userId) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false; if (epoch !== generation && user.value) void load() }
  }
  function apply(id: string | null) { selected.value = id; draft.value = poolDraft(questions.value.find(x => x.id === id)); original.value = JSON.stringify(draft.value); saved.value = false; uploadFiles.value = []; uploadKey.value++; deliveryPolicy.value = current.value?.flags?.some(x => x.specificationKind != null) ? 'RandomOnePerTeam' : 'All' }
  function select(id: string) { if (busy.value) return; if (dirty.value) { nextSelection = id; leaveOpen.value = true } else apply(id) }
  function create() { if (!writable.value || busy.value) return; if (dirty.value) { nextSelection = null; leaveOpen.value = true } else apply(null) }
  async function load() {
    if (busy.value || disposed) return
    const request = ++generation, actor = user.value?.userId; loading.value = true
    try {
      if (!context?.competition.value) await context?.refresh()
      const result = await listLiveSoloQuestionPool({ path: { competitionId: competitionId.value } })
      if (disposed || request !== generation || actor !== user.value?.userId) return
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
      questions.value = result.data.items ?? []
      if (!dirty.value || !original.value) apply(selected.value ?? questions.value[0]?.id ?? null)
      error.value = null
    } catch (cause) { if (!disposed && request === generation) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { if (request === generation) loading.value = false }
  }
  async function save() {
    if (!writable.value || busy.value) return
    const errors = poolDraftErrors(draft.value); if (errors.length) { error.value = errors[0]!; return }
    busy.value = true
    const actor = user.value?.userId, epoch = generation
    try {
      const path = { competitionId: competitionId.value }, value = { ...draft.value, title: draft.value.title.trim(), description: draft.value.description || null }
      const result = current.value?.id
        ? await editLiveSoloPoolQuestion({ path: { ...path, competitionChallengeId: current.value.id }, body: { ...value, expectedStamp: current.value.concurrencyStamp!, expectedMaterialStamp: current.value.materialStamp! } })
        : await createLiveSoloPoolQuestion({ path, body: value })
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      if (disposed || actor !== user.value?.userId || epoch !== generation) return
      questions.value = [...questions.value.filter(x => x.id !== result.data!.id), result.data]; apply(result.data.id ?? null); saved.value = true; error.value = null
    } catch (cause) { if (!disposed && epoch === generation && actor === user.value?.userId) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false; if (epoch !== generation && user.value) void load() }
  }
  async function searchSources() { sources.reset(); sourceId.value = null; await sources.loadPage() }
  async function openDeployment() { if (!writable.value || busy.value) return; deployOpen.value = true; await searchSources() }
  function setDeployment(value: boolean) { if (!busy.value) deployOpen.value = value }
  async function deploy() {
    const source = sources.items.value.find(x => x.id === sourceId.value)
    if (!writable.value || busy.value || !source?.id || !source.concurrencyStamp) return
    busy.value = true
    const actor = user.value?.userId, epoch = generation
    try {
      const result = await deployLiveSoloPoolQuestion({ path: { competitionId: competitionId.value }, body: { sourceTemplateId: source.id, expectedSourceStamp: source.concurrencyStamp } })
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      if (disposed || actor !== user.value?.userId || epoch !== generation) return
      questions.value = [...questions.value.filter(x => x.id !== result.data!.id), result.data]
      if (!dirty.value) apply(result.data.id ?? null)
      deployOpen.value = false; error.value = null
    } catch (cause) { if (!disposed && epoch === generation && actor === user.value?.userId) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false; if (epoch !== generation && user.value) void load() }
  }
  async function exportToBank() {
    if (!writable.value || !canOrganize.value || busy.value || dirty.value || !current.value?.id) return
    busy.value = true
    const actor = user.value?.userId, epoch = generation
    try {
      const result = await exportLiveSoloPoolQuestion({ path: { competitionId: competitionId.value, competitionChallengeId: current.value.id }, body: { expectedStamp: current.value.concurrencyStamp!, expectedMaterialStamp: current.value.materialStamp! } })
      if (result.error || !result.data?.challengeId) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      if (actor !== user.value?.userId || epoch !== generation) return
      allowLeave = true; await router.push(`/admin/challenges/${result.data.challengeId}`)
    } catch (cause) { if (!disposed && epoch === generation && actor === user.value?.userId) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false; if (epoch !== generation && user.value) void load() }
  }
  async function back() {
    const group = typeof route.query.group === 'string' ? route.query.group : null
    await router.push(group ? { path: `/competitions/${competitionId.value}/live-solo/groups${group === 'new' ? '' : '/' + group}`, query: { added: current.value?.id } } : `/competitions/${competitionId.value}/live-solo`)
  }
  function setLeave(value: boolean) { if (!busy.value) leaveOpen.value = value }
  async function discard() { leaveOpen.value = false; if (nextSelection !== undefined) { apply(nextSelection); nextSelection = undefined } else if (leaveTo) { allowLeave = true; await router.push(leaveTo) } }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(to => { if (allowLeave) return true; if (busy.value) return false; if (!dirty.value) return true; nextSelection = undefined; leaveTo = to.fullPath; leaveOpen.value = true; return false })
  watch(() => user.value?.userId, () => { generation++; questions.value = []; apply(null); sources.reset(); error.value = null; if (user.value) void load() })
  watch(writable, () => { generation++; questions.value = []; apply(null); sources.reset(); if (user.value) void load() })
  onMounted(() => { void load(); window.addEventListener('beforeunload', beforeUnload) })
  onScopeDispose(() => { disposed = true; generation++; window.removeEventListener('beforeunload', beforeUnload) })
  return { questions, options, current, selected, draft, dirty, writable, canOrganize, loading, busy, error, saved, returning, load, select, create, save, back,
    attachments, uploadFiles, uploadKey, deliveryPolicy, downloadFileName, addFlag, removeFlag, removeAttachment, chooseFiles, upload, deployOpen, setDeployment, openDeployment, sourceId, sourceOptions, keyword, searchSources, deploy, exportToBank,
    sourcePage: sources.page, sourcePageCount: sources.pageCount, sourceTotal: sources.total, sourceLimit: sources.limit, sourceLoading: sources.loading,
    sourcePageChange: sources.loadPage, sourceLimitChange: sources.setPageSize, leaveOpen, setLeave, discard, DefinitionEditor: markRaw(DefinitionEditor) }
}
export type LiveSoloQuestionPoolState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloQuestionPool>>
