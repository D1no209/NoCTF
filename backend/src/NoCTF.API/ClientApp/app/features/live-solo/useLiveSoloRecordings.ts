import { computed, onMounted, onScopeDispose, ref, watch } from 'vue'
import { changeLiveSoloRecording, listLiveSoloRecordings, listLiveSoloRecordingDecisions, prepareLiveSoloRecordingDownload, prepareLiveSoloRecordingPreview } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloRecordingResponse as Recording, NoCtfapiEndpointsLiveSoloLiveSoloRecordingDecisionResponse as Decision, NoCtfDomainLiveSoloLiveSoloRecordingAction as Action } from '~/api'
import { message, type UiMessage } from '~/utils/i18n'
import { startAttachmentBrowserDownload } from '~/utils/download'
import { parseLiveSoloError } from './live-solo-errors'
import { ApiError } from '~/utils/api-error'
import type { MessageKey } from '~/locales/en'
import { playableRecording, recordingActions, recordingActionKey, recordingFailureKey } from './recording-policy'

export function useLiveSoloRecordings() {
  const route = useRoute(), router = useRouter()
  const competitionId = computed(() => route.params.id as string), matchId = computed(() => route.params.matchId as string)
  const selectedId = computed(() => typeof route.params.recordingId === 'string' ? route.params.recordingId : null)
  const staff = computed(() => route.query.staff === '1')
  const canJudge = ref(false), canPublish = ref(false), error = ref<UiMessage | null>(null), mediaError = ref<UiMessage | null>(null), busy = ref(false)
  const source = ref<string | null>(null), history = ref<Decision[]>([]), reason = ref(''), dialog = ref(false)
  const decision = ref<{ row: Recording; action: Action } | null>(null)
  let disposed = false, selectedRequest = 0, renewAt = 0, timer: ReturnType<typeof setTimeout> | undefined
  const pagination = useOffsetPagination<Recording>(async ({ offset, limit }) => {
    const result = await listLiveSoloRecordings({ path: { competitionId: competitionId.value, matchId: matchId.value }, query: { staff: staff.value, offset, limit } })
    if (result.error || !result.data) { source.value = null; throw parseLiveSoloError(result.error, message('liveSolo.recording.loadFailed')) }
    if (disposed) return { items: [], total: 0 }
    canJudge.value = result.data.canJudge ?? false; canPublish.value = result.data.canPublish ?? false
    return result.data
  }, { initialPageSize: 20 })
  const current = computed(() => pagination.items.value.find(x => x.id === selectedId.value) ?? null)
  const options = computed(() => pagination.items.value.filter(x => x.id).map(row => ({ value: row.id!, label: row.userName ?? '—', row })))
  const actions = computed(() => recordingActions(current.value, canJudge.value, canPublish.value).map(action => ({ action, key: recordingActionKey[action] })))
  const playable = computed(() => playableRecording(current.value))
  const failureKey = computed(() => current.value?.failure ? recordingFailureKey[current.value.failure] : null)
  const historyRows = computed(() => history.value.map(entry => ({ entry, actionKey: (entry.action ? recordingActionKey[entry.action] : 'liveSolo.recording.decision') as MessageKey })))
  const actionKey = computed(() => decision.value ? recordingActionKey[decision.value.action] : 'liveSolo.recording.decision')
  async function select(id: string) { await router.replace({ path: `/competitions/${competitionId.value}/live-solo/recordings/${matchId.value}/${id}`, query: { ...route.query } }) }
  async function openReplacement(id: string) { source.value = null; await pagination.loadPage(1); if (!disposed) await select(id) }
  async function readSelected(renew = false) {
    const row = current.value; const sequence = ++selectedRequest
    if (!renew) { source.value = null; history.value = []; mediaError.value = null; renewAt = 0 }
    if (!row?.id) { source.value = null; return }
    try {
      if (staff.value && !renew) {
        const result = await listLiveSoloRecordingDecisions({ path: { competitionId: competitionId.value, matchId: matchId.value, recordingId: row.id } })
        if (disposed || sequence !== selectedRequest || current.value?.id !== row.id) return
        if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.recording.loadFailed'))
        history.value = result.data.items ?? []
      }
      if (!playableRecording(row)) { source.value = null; return }
      if (staff.value) {
        const path = { competitionId: competitionId.value, matchId: matchId.value, recordingId: row.id }
        const preview = await prepareLiveSoloRecordingPreview({ path })
        if (disposed || sequence !== selectedRequest || current.value?.id !== row.id) return
        if (preview.error || !preview.data?.previewUrl) throw parseLiveSoloError(preview.error, message('liveSolo.recording.loadFailed'))
        source.value = preview.data.previewUrl; renewAt = Date.now() + 240_000
      }
      else source.value = row.fileUrl ?? null
    }
    catch (cause) { if (!disposed && sequence === selectedRequest) { source.value = null; mediaError.value = parseLiveSoloError(cause, message('liveSolo.recording.loadFailed')).displayMessage } }
  }
  async function load() {
    await pagination.loadPage()
    if (disposed) return
    if (pagination.error.value) { source.value = null; return }
    if (selectedId.value && !current.value) { source.value = null; history.value = []; return }
    if (!selectedId.value && options.value[0]) await select(options.value[0].value)
    if (source.value && staff.value && Date.now() >= renewAt) await readSelected(true)
  }
  async function download() {
    if (!current.value?.id || busy.value || !playable.value) return
    busy.value = true
    try {
      if (staff.value) {
        const result = await prepareLiveSoloRecordingDownload({ path: { competitionId: competitionId.value, matchId: matchId.value, recordingId: current.value.id } })
        if (result.error || !result.data?.downloadUrl) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
        if (!disposed) startAttachmentBrowserDownload(result.data.downloadUrl)
      }
      else if (current.value.fileUrl) startAttachmentBrowserDownload(current.value.fileUrl + '?download=true')
    }
    catch (cause) { if (!disposed) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false }
  }
  function begin(action: Action) { if (!current.value || !actions.value.some(x => x.action === action)) return; error.value = null; decision.value = { row: { ...current.value }, action }; reason.value = ''; dialog.value = true }
  function setDialog(value: boolean) { if (busy.value) return; dialog.value = value; if (!value) decision.value = null }
  async function confirm() {
    const target = decision.value
    if (!target?.row.id || !target.row.concurrencyStamp || busy.value) return
    if (!reason.value.trim()) { error.value = message('liveSolo.judge.reasonRequired'); return }
    busy.value = true
    try {
      const result = await changeLiveSoloRecording({ path: { competitionId: competitionId.value, matchId: matchId.value, recordingId: target.row.id },
        body: { action: target.action, expectedStamp: target.row.concurrencyStamp, reason: reason.value.trim() } })
      if (result.error || !result.data) {
        if (result.response?.status === 409) { dialog.value = false; decision.value = null; await load(); throw new ApiError(message('liveSolo.recording.reviewRequired')) }
        throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      }
      dialog.value = false; decision.value = null; await load(); await readSelected()
    }
    catch (cause) { if (!disposed) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false }
  }
  function failed() { mediaError.value = message('liveSolo.program.failed') }
  async function retry() { mediaError.value = null; source.value = null; await readSelected() }
  async function page(value: number) { source.value = null; await pagination.loadPage(value); if (options.value[0]) await select(options.value[0].value) }
  async function pageSize(value: number) { source.value = null; await pagination.setPageSize(value); if (options.value[0]) await select(options.value[0].value) }
  async function back() { await router.push({ path: `/competitions/${competitionId.value}/live-solo`, query: { match: matchId.value } }) }
  watch(() => [current.value?.id, current.value?.state, staff.value], () => { void readSelected() })
  async function tick() { await load(); if (!disposed) timer = setTimeout(tick, 15_000) }
  onMounted(() => { void tick() })
  onScopeDispose(() => { disposed = true; selectedRequest++; source.value = null; if (timer) clearTimeout(timer) })
  return { options, selectedId, current, source, history, historyRows, actions, failureKey, playable, staff, error, mediaError, busy, dialog, reason, actionKey, decision,
    loading: pagination.loading, listError: pagination.error, pageNumber: pagination.page, pageCount: pagination.pageCount, total: pagination.total, limit: pagination.limit,
    select, openReplacement, load, download, begin, setDialog, confirm, retry, failed, page, pageSize, back }
}
export type LiveSoloRecordingsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloRecordings>>
