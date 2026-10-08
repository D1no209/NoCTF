import { onBeforeRouteLeave, onBeforeRouteUpdate } from 'vue-router'
import { useNow } from '@vueuse/core'
import { listChallengeWriteUps, getChallengeWriteUpContent, prepareChallengeWriteUpBrowserAccess,
  saveChallengeWriteUpDraft, saveChallengeWriteUpPdfDraft, submitChallengeWriteUp, reviewChallengeWriteUp, getCompetitionEndpoint } from '~/api'
import type { NoCtfDomainChallengesWriteUpsWriteUpFormat, NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'
import { message } from '~/utils/i18n'
import type { UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import { latestWriteUpVersion, writeUpStatusKey } from './writeup-state'
import type { WriteUp, WriteUpAccess, WriteUpContent } from './writeup-state'

export function useChallengeWriteUpEditor(props: Readonly<{ competitionId: string; competitionChallengeId: string; official?: boolean }>, saved: () => void) {
  const root = ref<WriteUp | null>(null), access = ref<WriteUpAccess | null>(null)
  const loading = ref(true), pending = ref(false), dirty = ref(false)
  const error = ref<UiMessage | null>(null)
  const markdown = ref(''), format = ref<NoCtfDomainChallengesWriteUpsWriteUpFormat>('Markdown')
  const file = ref<File | null>(null), pdfUrl = ref<string | null>(null), fileName = ref('')
  const uploadKey = ref(0), leaveOpen = ref(false)
  const now = useNow({ interval: 1000 })
  const historyId = ref(''), historyContent = ref<WriteUpContent | null>(null), historyPdf = ref<string | null>(null), historyLoading = ref(false)
  const publishOpen = ref(false)
  const competitionStatus = ref<NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol | null>(null)
  const canSave = computed(() => props.official ? access.value?.canManage === true && access.value.settings?.enabled === true : access.value?.canSubmit === true
    && !!access.value.settings?.deadlineAt && now.value.getTime() <= Date.parse(access.value.settings.deadlineAt))
  const currentVersion = computed(() => latestWriteUpVersion(root.value))
  const statusKey = computed(() => writeUpStatusKey(root.value))
  const deadlineAt = computed(() => access.value?.settings?.deadlineAt)
  const canSubmit = computed(() => canSave.value && !!root.value?.draft && !dirty.value && !pending.value)
  const historyVersion = computed(() => root.value?.versions?.find(x => x.id === historyId.value))
  const historyOptions = computed(() => (root.value?.versions ?? []).filter(x => x.id !== currentVersion.value?.id))
  const publicationVersion = computed(() => root.value?.draft ?? root.value?.submitted)
  const canPublish = computed(() => props.official && canSave.value && (!!root.value?.draft || root.value?.submitted?.state === 'Submitted') && !dirty.value && !pending.value)
  let sequence = 0, historySequence = 0, loadingDraft = false, resolveLeave: ((value: boolean) => void) | undefined
  async function load() {
    if (pending.value) return
    const request = ++sequence; loading.value = true; error.value = null; pdfUrl.value = null; historyId.value = ''
    const result = await listChallengeWriteUps({ path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId }, query: { staff: props.official === true } })
    if (request !== sequence) return
    if (result.error || !result.data?.access) { loading.value = false; error.value = parseApiError(result.error, message('challengeWriteUp.loadFailed')).displayMessage; return }
    access.value = result.data.access
    root.value = result.data.items?.find(row => props.official ? row.source === 'Official' : row.teamId === access.value?.teamId) ?? null
    const version = latestWriteUpVersion(root.value)
    loadingDraft = true
    markdown.value = ''; file.value = null; fileName.value = ''; format.value = version?.format ?? 'Markdown'
    if (version?.id) {
      const content = await getChallengeWriteUpContent({ path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId, versionId: version.id }, query: { staff: props.official === true } })
      if (request !== sequence) return
      if (content.error || !content.data) error.value = parseApiError(content.error, message('challengeWriteUp.readFailed')).displayMessage
      else {
        markdown.value = content.data.markdown ?? ''; fileName.value = content.data.fileName ?? ''
        if (version.format === 'Pdf') {
          const grant = await prepareChallengeWriteUpBrowserAccess({ path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId, versionId: version.id }, body: { staff: props.official === true } })
          if (request !== sequence) return
          if (grant.data?.previewUrl) pdfUrl.value = grant.data.previewUrl
          else error.value = parseApiError(grant.error, message('challengeWriteUp.readFailed')).displayMessage
        }
      }
    }
    await nextTick()
    dirty.value = false; loadingDraft = false; loading.value = false; uploadKey.value++
  }
  watch([markdown, format], () => { if (!loadingDraft && !loading.value) dirty.value = true }, { flush: 'sync' })
  function selectFile(event: Event) {
    file.value = (event.target as HTMLInputElement).files?.[0] ?? null
    if (!file.value) return
    if (!file.value.name.toLowerCase().endsWith('.pdf') || file.value.size > 64 * 1024 * 1024) {
      file.value = null; error.value = message('challengeWriteUp.error.InvalidContent'); return
    }
    dirty.value = true; error.value = null; fileName.value = file.value.name
  }
  async function save() {
    if (!canSave.value || pending.value || loading.value) return
    if (!dirty.value && root.value?.draft) return
    if (format.value === 'Markdown' ? !markdown.value.trim() : !file.value) { error.value = message('challengeWriteUp.bodyRequired'); return }
    pending.value = true; error.value = null
    const path = { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId }
    const common = { official: props.official === true, expectedStamp: root.value?.concurrencyStamp ?? null }
    const request = sequence
    try {
      const result = format.value === 'Pdf'
        ? await saveChallengeWriteUpPdfDraft({ path, body: { ...common, file: file.value! } })
        : await saveChallengeWriteUpDraft({ path, body: { ...common, markdown: markdown.value } })
      if (request !== sequence) return
      if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.saveFailed')).displayMessage; return }
      root.value = result.data; dirty.value = false; file.value = null; uploadKey.value++
      toast.success(message('challengeWriteUp.saved')); saved()
      if (format.value === 'Pdf' && root.value.draft?.id) {
        const grant = await prepareChallengeWriteUpBrowserAccess({ path: { ...path, versionId: root.value.draft.id }, body: { staff: props.official === true } })
        if (request === sequence) pdfUrl.value = grant.data?.previewUrl ?? null
      }
    }
    finally { if (request === sequence) pending.value = false }
  }
  async function submit() {
    if (!canSubmit.value || !root.value?.concurrencyStamp) return
    pending.value = true; error.value = null
    const result = await submitChallengeWriteUp({ path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
      body: { official: props.official === true, expectedStamp: root.value.concurrencyStamp } })
    pending.value = false
    if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.saveFailed')).displayMessage; return }
    root.value = result.data; toast.success(message('challengeWriteUp.submittedNotice')); saved()
  }
  async function viewHistory(value: unknown) {
    let id = String(value ?? '')
    if (id === 'current') id = ''
    historyId.value = id; historyContent.value = null; historyPdf.value = null
    const request = ++historySequence
    if (!id) return
    historyLoading.value = true; error.value = null
    const path = { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId, versionId: id }
    try {
      const result = await getChallengeWriteUpContent({ path, query: { staff: props.official === true } })
      if (request !== historySequence) return
      if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.readFailed')).displayMessage; return }
      historyContent.value = result.data
      if (result.data.format === 'Pdf') {
        const grant = await prepareChallengeWriteUpBrowserAccess({ path, body: { staff: props.official === true } })
        if (request !== historySequence) return
        historyPdf.value = grant.data?.previewUrl ?? null
        if (grant.error) error.value = parseApiError(grant.error, message('challengeWriteUp.readFailed')).displayMessage
      }
    }
    catch (cause) { if (request === historySequence) error.value = parseApiError(cause, message('challengeWriteUp.readFailed')).displayMessage }
    finally { if (request === historySequence) historyLoading.value = false }
  }
  async function requestPublish() {
    if (!canPublish.value) return
    const result = await getCompetitionEndpoint({ path: { competitionId: props.competitionId } })
    competitionStatus.value = result.data?.status ?? null
    if (canPublish.value) publishOpen.value = true
  }
  function setPublishOpen(value: boolean) { if (!pending.value) publishOpen.value = value }
  async function publish() {
    if (!canPublish.value || !root.value?.id || !publicationVersion.value?.id || !root.value.concurrencyStamp) return
    pending.value = true; error.value = null
    try {
      if (root.value.draft) {
        const submission = await submitChallengeWriteUp({ path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId },
          body: { official: true, expectedStamp: root.value.concurrencyStamp } })
        if (submission.error || !submission.data) { error.value = parseApiError(submission.error, message('challengeWriteUp.saveFailed')).displayMessage; return }
        root.value = submission.data
      }
      if (!root.value.id || !root.value.submitted?.id || !root.value.concurrencyStamp) return
      const result = await reviewChallengeWriteUp({ path: { competitionId: props.competitionId, competitionChallengeId: props.competitionChallengeId, writeUpId: root.value.id },
        body: { action: 'Publish', versionId: root.value.submitted.id, expectedStamp: root.value.concurrencyStamp } })
      if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.reviewFailed')).displayMessage; return }
      root.value = result.data; publishOpen.value = false; toast.success(message('challengeWriteUp.reviewSaved')); saved()
    }
    finally { pending.value = false }
  }
  function confirmDiscard(): Promise<boolean> {
    if (pending.value) return Promise.resolve(false)
    if (!dirty.value) return Promise.resolve(true)
    leaveOpen.value = true
    return new Promise(resolve => { resolveLeave = resolve })
  }
  function stay() { leaveOpen.value = false; resolveLeave?.(false); resolveLeave = undefined }
  function discard() { dirty.value = false; leaveOpen.value = false; resolveLeave?.(true); resolveLeave = undefined }
  function setLeaveOpen(open: boolean) { if (!open) stay() }
  async function reload() { if (await confirmDiscard()) await load() }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(confirmDiscard); onBeforeRouteUpdate(confirmDiscard)
  onMounted(() => { window.addEventListener('beforeunload', beforeUnload); void load() })
  onBeforeUnmount(() => { sequence++; historySequence++; window.removeEventListener('beforeunload', beforeUnload); resolveLeave?.(false) })
  return { root, access, loading, pending, dirty, error, markdown, format, pdfUrl, fileName, uploadKey, leaveOpen,
    canSave, canSubmit, currentVersion, statusKey, deadlineAt, selectFile, save, submit, reload, stay, discard, setLeaveOpen, confirmDiscard,
    historyId, historyContent, historyPdf, historyLoading, historyVersion, historyOptions, viewHistory, publishOpen, canPublish, publicationVersion, competitionStatus, requestPublish, setPublishOpen, publish,
    official: computed(() => props.official === true) }
}
export type ChallengeWriteUpEditorState = import('vue').ShallowUnwrapRef<ReturnType<typeof useChallengeWriteUpEditor>>
