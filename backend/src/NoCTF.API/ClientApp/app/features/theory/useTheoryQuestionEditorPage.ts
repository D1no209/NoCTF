import { theoryQuestionsGet, theoryQuestionsCreate, theoryQuestionsUpdate, theoryQuestionsSetState, theoryQuestionsCopy, theoryQuestionsGrant, uploadTheoryImage } from '~/api'
import type { NoCtfApplicationTheoryQuestionsTheoryQuestionInput as QuestionInput, NoCtfApplicationTheoryQuestionsTheoryQuestionContent as QuestionDetail, NoCtfDomainTheoryQuestionsTheoryQuestionKind as QuestionKind, NoCtfDomainTheoryQuestionsTheoryQuestionState as QuestionState, NoCtfDomainTheoryQuestionsTheoryQuestionPermission as Permission } from '~/api'
import { parseApiError } from '~/utils/api-error'
import { message, type UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import { useTheoryAssets } from './useTheoryAssets'
import { questionKindLabel } from './theory-labels'

export function useTheoryQuestionEditorPage() {
  const route = useRoute(), router = useRouter(), { canOrganize } = useAuth()
  const id = computed(() => typeof route.params.questionId === 'string' ? route.params.questionId : null)
  const detail = ref<QuestionDetail | null>(null), busy = ref(false), loading = ref(false), error = ref<UiMessage | null>(null)
  const form = reactive<QuestionInput>({ title: '', body: '', explanation: '', kind: 0, difficulty: 0,
    options: [{ content: '', isCorrect: true }, { content: '', isCorrect: false }], classifications: [], fileIds: [] })
  const knowledge = ref(''), tags = ref(''), baseline = ref(''), permissionUser = ref(''), permission = ref('0')
  const explanation = computed({ get: () => form.explanation ?? '', set: (value: string) => { form.explanation = value } })
  const files = useTheoryAssets(), previewOrder = ref<number[]>([0, 1])
  const snapshot = () => JSON.stringify({ form, knowledge: knowledge.value, tags: tags.value })
  const dirty = computed(() => baseline.value !== '' && snapshot() !== baseline.value)
  const leaveOpen = ref(false); let resolveLeave: ((allow: boolean) => void) | undefined
  let epoch = 0
  async function load() {
    const current = ++epoch
    if (!id.value) { baseline.value = snapshot(); return }
    loading.value = true; error.value = null
    try {
      const response = await theoryQuestionsGet({ path: { questionId: id.value } })
      if (response.error || !response.data?.content) throw response.error
      if (current !== epoch) return
      detail.value = response.data; Object.assign(form, structuredClone(response.data.content))
      knowledge.value = form.classifications.filter(item => item.kind === 0).map(item => item.name).join(', ')
      tags.value = form.classifications.filter(item => item.kind === 1).map(item => item.name).join(', ')
      previewOrder.value = form.options.map((_, index) => index); baseline.value = snapshot()
      void files.load([form.body, form.explanation ?? '', ...form.options.map(option => option.content ?? '')], { questionId: id.value })
    }
    catch (cause) { if (current === epoch) error.value = parseApiError(cause, message('theory.loadFailed')).displayMessage }
    finally { if (current === epoch) loading.value = false }
  }
  function changeKind(value: unknown) {
    const kind = Number(value); if (kind !== 0 && kind !== 1 && kind !== 2) return
    form.kind = kind
    if (kind === 2) form.options = [{ content: 'True', isCorrect: true, truthValue: 0 }, { content: 'False', isCorrect: false, truthValue: 1 }]
    else { form.options = form.options.map(option => ({ content: option.content, isCorrect: option.isCorrect })); if (kind === 0) setCorrect(form.options.findIndex(option => option.isCorrect), true) }
    previewOrder.value = form.options.map((_, index) => index)
  }
  function setCorrect(index: number, value: unknown) {
    if (form.kind !== 1 && value === true) form.options.forEach((option, current) => { option.isCorrect = current === index })
    else if (form.options[index]) form.options[index]!.isCorrect = value === true
  }
  function addOption() { if (form.options.length < 8) { form.options.push({ content: '', isCorrect: false }); previewOrder.value = form.options.map((_, index) => index) } }
  function removeOption(index: number) { if (form.options.length > 2) { form.options.splice(index, 1); previewOrder.value = form.options.map((_, index) => index) } }
  function shufflePreview() { const indexes = form.options.map((_, index) => index); for (let index = indexes.length - 1; index > 0; index--) { const next = Math.floor(Math.random() * (index + 1)); [indexes[index], indexes[next]] = [indexes[next]!, indexes[index]!] } previewOrder.value = indexes }
  function classifications(text: string, kind: 0 | 1) { return [...new Set(text.split(/[,，]/).map(value => value.trim()).filter(Boolean))].map(name => ({ kind, name })) }
  async function save() {
    if (busy.value) return
    busy.value = true; error.value = null
    try {
      const content: QuestionInput = { ...form, classifications: [...classifications(knowledge.value, 0), ...classifications(tags.value, 1)] }
      const response = id.value ? await theoryQuestionsUpdate({ path: { questionId: id.value }, body: { content } }) : await theoryQuestionsCreate({ body: { content } })
      if (response.error || !response.data?.questionId) throw response.error
      baseline.value = snapshot(); toast.success(message('theory.saved'))
      if (!id.value) await router.replace(`/admin/theory/questions/${response.data.questionId}`)
      else await load()
    }
    catch (cause) { error.value = parseApiError(cause, message('theory.saveFailed')).displayMessage }
    finally { busy.value = false }
  }
  async function setState(value: unknown) {
    if (!id.value || busy.value) return
    const state = Number(value); if (![0, 1, 2].includes(state)) return
    busy.value = true
    try { const result = await theoryQuestionsSetState({ path: { questionId: id.value }, body: { state: state as QuestionState } }); if (result.error) throw result.error; if (detail.value?.question) detail.value.question.state = state as QuestionState }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { busy.value = false }
  }
  async function copy() {
    if (!id.value || busy.value || dirty.value) return
    busy.value = true
    try { const result = await theoryQuestionsCopy({ path: { questionId: id.value } }); if (result.error || !result.data?.questionId) throw result.error; await router.push(`/admin/theory/questions/${result.data.questionId}`) }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { busy.value = false }
  }
  async function grant(userId = permissionUser.value, remove = false) {
    if (!id.value || busy.value || !userId.trim()) return
    busy.value = true
    try { const result = await theoryQuestionsGrant({ path: { questionId: id.value, userId: userId.trim() }, body: { permission: remove ? null : Number(permission.value) as Permission } }); if (result.error) throw result.error; permissionUser.value = ''; const refreshed = await theoryQuestionsGet({ path: { questionId: id.value } }); if (refreshed.data) detail.value = refreshed.data; toast.success(message('theory.saved')) }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { busy.value = false }
  }
  async function selectFile(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0]
    if (!file || !id.value || busy.value) return
    busy.value = true
    try {
      const result = await uploadTheoryImage({ path: { questionId: id.value }, body: { file } })
      if (result.error || !result.data?.fileId) throw result.error
      form.fileIds.push(result.data.fileId); form.body += `\n![image](theory-file:${result.data.fileId})\n`
      await files.load([form.body, ...form.options.map(option => option.content ?? '')], { questionId: id.value })
    }
    catch (cause) { error.value = parseApiError(cause).displayMessage }
    finally { busy.value = false }
  }
  function confirmLeave() { if (!dirty.value) return Promise.resolve(true); leaveOpen.value = true; return new Promise<boolean>(resolve => { resolveLeave = resolve }) }
  function stay() { leaveOpen.value = false; resolveLeave?.(false); resolveLeave = undefined }
  function discard() { baseline.value = snapshot(); leaveOpen.value = false; resolveLeave?.(true); resolveLeave = undefined }
  function setLeaveOpen(open: boolean) { if (!open) stay() }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(confirmLeave); onBeforeRouteUpdate(confirmLeave)
  watch(id, () => { files.clear(); void load() })
  onMounted(() => { void load(); window.addEventListener('beforeunload', beforeUnload) })
  onBeforeUnmount(() => { epoch++; window.removeEventListener('beforeunload', beforeUnload); resolveLeave?.(false) })
  return { id, detail, form, explanation, knowledge, tags, busy, loading, error, dirty, canOrganize, permissionUser, permission, previewOrder,
    assets: files.assets, load, save, changeKind, setCorrect, addOption, removeOption, shufflePreview, setState, copy, grant, selectFile,
    leaveOpen, stay, discard, setLeaveOpen, back: () => router.push('/admin/theory/questions'), questionKindLabel,
    revokeGrant: (userId: string | undefined) => userId ? grant(userId, true) : undefined,
    optionLabel: (index: number) => String.fromCharCode(65 + index), kinds: [0, 1, 2] as QuestionKind[] }
}
export type TheoryQuestionEditorState = import('vue').ShallowUnwrapRef<ReturnType<typeof useTheoryQuestionEditorPage>>
