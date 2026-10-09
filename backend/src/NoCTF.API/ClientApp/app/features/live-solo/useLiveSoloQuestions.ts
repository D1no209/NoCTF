import { computed, markRaw, onMounted, onScopeDispose, ref, watch } from 'vue'
import { listLiveSoloQuestions, listLiveSoloAttachments, prepareLiveSoloAttachmentDownload, prepareLiveSoloRandomAttachmentDownload,
  submitLiveSoloFlag, getGameplayFactStatusEndpoint, getLiveSoloRuntime, mutateLiveSoloRuntime } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloQuestionResponse as Question, NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse as Round,
  NoCtfapiEndpointsLiveSoloLiveSoloAttachmentsResponse as Attachments, NoCtfapiEndpointsRuntimeRuntimeResponse as Runtime,
  NoCtfapiEndpointsLiveSoloLiveSoloRuntimeActionProtocol as RuntimeAction } from '~/api'
import { useHumanVerification } from '~/features/security/useHumanVerification'
import RuntimeAccessUrl from '~/features/challenges/RuntimeAccessUrl.vue'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { startAttachmentBrowserDownload } from '~/utils/download'
import { gameplayFactResultLabel, gameplayFactFailureCodeLabel } from '~/utils/labels'
import { questionFromWorkspaceRoute } from './live-solo-state'

export function useLiveSoloQuestions(props: Readonly<{ competitionId: string; matchId: string; round: Round; canSubmit: boolean; dockTarget: string }>) {
  const route = useRoute(), router = useRouter(), { user } = useAuth()
  const { request: verification } = useHumanVerification()
  const questions = ref<Question[]>([]), selected = ref<string | null>(null), loading = ref(true), error = ref<UiMessage | null>(null)
  const attachments = ref<Attachments | null>(null), runtime = ref<Runtime | null>(null), resourceError = ref<UiMessage | null>(null)
  const submitting = ref(false), runtimeBusy = ref(false), downloading = ref(false), feedback = ref<UiMessage | null>(null)
  const drafts = useState<Record<string, string>>('live-solo:flag-drafts', () => ({}))
  const scopeKey = () => `${user.value?.userId}:${props.competitionId}:${props.matchId}:${props.round.id}`
  const input = computed({ get: () => drafts.value[`${scopeKey()}:${selected.value}`] ?? '',
    set: (value: string) => { if (selected.value) drafts.value[`${scopeKey()}:${selected.value}`] = value } })
  const current = computed(() => questions.value.find(x => x.id === selected.value) ?? null)
  const options = computed(() => questions.value.filter(x => x.id).map(row => ({ value: row.id!, label: row.title ?? '—' })))
  let disposed = false, request = 0, resourceRequest = 0, pending = false, timer: ReturnType<typeof setTimeout> | undefined
  let accepted: { id: string; sequence: number } | null = null
  const path = (questionId: string) => ({ competitionId: props.competitionId, matchId: props.matchId, roundId: props.round.id!, questionId })
  const statusPolling = usePolling(async () => {
    if (!accepted) return true
    const record = accepted
    const result = await getGameplayFactStatusEndpoint({ path: { competitionId: props.competitionId, gameplayFactId: record.id } })
    if (disposed || accepted !== record) return true
    if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
    if (result.data.state !== 'Completed' && result.data.state !== 'PlatformFailed') return false
    feedback.value = message('liveSolo.flagResult', { sequence: record.sequence,
      result: result.data.failureCode ? gameplayFactFailureCodeLabel(result.data.failureCode) : gameplayFactResultLabel(result.data.result ?? undefined) })
    accepted = null; return true
  })
  async function resources() {
    const questionId = selected.value, id = ++resourceRequest
    attachments.value = null; runtime.value = null; resourceError.value = null
    if (!questionId || !props.round.id) return
    const [files, environment] = await Promise.all([listLiveSoloAttachments({ path: path(questionId) }), getLiveSoloRuntime({ path: path(questionId) })])
    if (disposed || id !== resourceRequest || selected.value !== questionId) return
    if (files.error) resourceError.value = parseLiveSoloError(files.error, message('liveSolo.error.load')).displayMessage
    else attachments.value = files.data ?? null
    if (environment.response?.status !== 404 && environment.error) resourceError.value = parseLiveSoloError(environment.error, message('liveSolo.error.load')).displayMessage
    else runtime.value = environment.data ?? null
  }
  async function load() {
    if (disposed || pending || !props.round.id) return
    pending = true; const id = ++request
    try {
      const result = await listLiveSoloQuestions({ path: { competitionId: props.competitionId, matchId: props.matchId, roundId: props.round.id } })
      if (disposed || id !== request) return
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
      questions.value = result.data.items ?? []; error.value = null
      syncSelection()
    }
    catch (cause) {
      if (!disposed && id === request) { questions.value = []; selected.value = null; attachments.value = null; runtime.value = null; error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    }
    finally { pending = false; loading.value = false }
  }
  function syncSelection() {
    selected.value = questionFromWorkspaceRoute(route.params.workspace, props.round.id, selected.value, questions.value)
  }
  async function select(id: string) {
    if (!questions.value.some(x => x.id === id)) return
    selected.value = id
    await router.replace({ path: `/competitions/${props.competitionId}/live-solo/matches/${props.matchId}/rounds/${props.round.id}/questions/${id}`, query: { ...route.query } })
  }
  async function submit() {
    if (!props.canSubmit || submitting.value || accepted || !current.value?.id || !input.value.trim()) return
    const questionId = current.value.id, key = `${scopeKey()}:${questionId}`, flag = input.value
    const snapshot = path(questionId)
    submitting.value = true; feedback.value = null; error.value = null
    try {
      const headers = await verification('evaluation')
      if (!headers || disposed || !props.canSubmit || props.round.id !== snapshot.roundId) return
      const result = await submitLiveSoloFlag({ path: snapshot, body: { flag }, headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } })
      if (disposed) return
      if (result.error || !result.data?.gameplayFactId) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      accepted = { id: result.data.gameplayFactId, sequence: result.data.admissionSequence ?? 0 }
      feedback.value = message('liveSolo.flagAccepted', { sequence: accepted.sequence })
      if (drafts.value[key] === flag) delete drafts.value[key]
      statusPolling.start()
    }
    catch (cause) { if (!disposed) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { submitting.value = false }
  }
  async function download(fileId?: string) {
    if (downloading.value || !selected.value) return
    downloading.value = true; resourceError.value = null
    try {
      const scope = path(selected.value)
      const result = fileId ? await prepareLiveSoloAttachmentDownload({ path: { ...scope, attachmentId: fileId } })
        : await prepareLiveSoloRandomAttachmentDownload({ path: scope })
      if (result.error || !result.data?.downloadUrl) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      if (!disposed) startAttachmentBrowserDownload(result.data.downloadUrl)
    }
    catch (cause) { if (!disposed) resourceError.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { downloading.value = false }
  }
  const runtimePolling = usePolling(async () => {
    await resources()
    return runtime.value?.state !== 'Queued' && runtime.value?.state !== 'Provisioning' && runtime.value?.state !== 'Stopping'
  }, { timeout: 180000 })
  async function environment(action: RuntimeAction) {
    if (runtimeBusy.value || !props.canSubmit || !selected.value) return
    const scope = path(selected.value), expected = runtime.value?.id
    if (action !== 'Start' && !expected) return
    runtimeBusy.value = true; resourceError.value = null
    try {
      const headers = await verification('runtime')
      if (!headers || disposed || !props.canSubmit || props.round.id !== scope.roundId) return
      const result = await mutateLiveSoloRuntime({ path: scope, body: { action, expectedRuntimeInstanceId: expected ?? null }, headers: { ...headers, 'Idempotency-Key': crypto.randomUUID() } })
      if (result.error) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      if (selected.value === scope.questionId) runtimePolling.start()
    }
    catch (cause) { if (!disposed) resourceError.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { runtimeBusy.value = false }
  }
  async function tick() { await load(); if (!disposed) timer = setTimeout(tick, 5000) }
  watch(selected, () => { runtimePolling.stop(); void resources() })
  watch(() => route.params.workspace, syncSelection)
  watch(() => user.value?.userId, () => { drafts.value = {}; questions.value = []; selected.value = null })
  onMounted(() => { void tick() })
  onScopeDispose(() => { disposed = true; request++; resourceRequest++; if (timer) clearTimeout(timer); accepted = null })
  return { loading, error, current, selected, options, select, input, submitting, submit, feedback, attachments, runtime,
    resourceError, downloading, download, runtimeBusy, environment, RuntimeAccess: markRaw(RuntimeAccessUrl),
    canSubmit: computed(() => props.canSubmit), statusPending: statusPolling.polling, statusTimedOut: statusPolling.timedOut,
    inputPending: computed(() => submitting.value || statusPolling.polling.value),
    mayStartRuntime: computed(() => runtime.value?.state === 'Stopped' || runtime.value?.state === 'Failed'),
    retryStatus: statusPolling.start, dockSelector: computed(() => `#${props.dockTarget}`) }
}
export type LiveSoloQuestionsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloQuestions>>
