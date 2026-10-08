import { markRaw } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { ArrowLeft, BookOpen, Download, RefreshCw } from '@lucide/vue'
import { getChallengeEndpoint, listChallengeWriteUps, getChallengeWriteUpQuote, unlockChallengeWriteUp, getChallengeWriteUpContent,
  prepareChallengeWriteUpBrowserAccess } from '~/api'
import { message } from '~/utils/i18n'
import type { UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import { startWriteUpBrowserDownload } from '~/utils/download'
import CompetitionParticipantWorkspaceComponent from '~/features/competition/CompetitionParticipantWorkspace.vue'
import ChallengeWriteUpEditorComponent from './ChallengeWriteUpEditor.vue'
import { challengeWriteUpPath, needsWriteUpConfirmation } from './writeup-state'
import type { WriteUp, WriteUpAccess, WriteUpContent, WriteUpQuote } from './writeup-state'

export function useChallengeWriteUpPage(props: Readonly<{ mine?: boolean }>) {
  const route = useRoute(), router = useRouter()
  const competitionId = String(route.params.id), challengeId = String(route.params.competitionChallengeId)
  const ctx = inject(competitionContextKey)!
  const items = ref<WriteUp[]>([]), access = ref<WriteUpAccess | null>(null)
  const selectedId = computed(() => String(route.params.writeUpId ?? ''))
  const selected = computed(() => items.value.find(x => x.id === selectedId.value) ?? null)
  const loading = ref(true), reading = ref(false), downloading = ref(false), confirmPending = ref(false)
  const error = ref<UiMessage | null>(null), contentError = ref<UiMessage | null>(null)
  const content = ref<WriteUpContent | null>(null), pdfUrl = ref<string | null>(null), quote = ref<WriteUpQuote | null>(null)
  const confirmOpen = ref(false), search = ref('')
  const pdfKey = ref(0)
  const mine = computed(() => props.mine === true)
  const narrow = useMediaQuery('(max-width: 1023px)')
  const disabled = computed(() => ctx.competition.value?.singleWriteUpsEnabled === false)
  const mode = computed(() => ctx.competition.value?.mode)
  const challengeTitle = ref('')
  const title = computed(() => selected.value?.challengeTitle ?? items.value[0]?.challengeTitle ?? challengeTitle.value)
  const options = computed(() => items.value.filter(x => `${x.authorName ?? ''} ${x.source === 'Official' ? translate('challengeWriteUp.official') : ''}`
    .toLocaleLowerCase().includes(search.value.toLocaleLowerCase())).map(x => ({ value: x.id ?? '', label: x.source === 'Official'
      ? translate('challengeWriteUp.official') : x.authorName ?? '', row: x })))
  const own = computed(() => !!selected.value?.teamId && selected.value.teamId === access.value?.teamId)
  const retained = computed(() => 100 - (access.value?.deductionPercent ?? 20))
  const benefit = computed(() => access.value?.canShowCurrentScore === false ? null
    : ctx.standing.value?.challengeBenefits?.find(x => x.competitionChallengeId === challengeId) ?? null)
  let loadSequence = 0, readSequence = 0, unwatch: (() => void) | undefined
  function clearContent() { readSequence++; content.value = null; pdfUrl.value = null; quote.value = null; confirmOpen.value = false; contentError.value = null; reading.value = false }
  async function load() {
    const request = ++loadSequence; loading.value = items.value.length === 0; error.value = null
    const [result, challenge] = await Promise.all([listChallengeWriteUps({ path: { competitionId, competitionChallengeId: challengeId }, query: { staff: false } }), getChallengeEndpoint({ path: { competitionId, competitionChallengeId: challengeId } })])
    if (request !== loadSequence) return
    loading.value = false
    if (result.error || !result.data) { clearContent(); error.value = parseApiError(result.error, message('challengeWriteUp.loadFailed')).displayMessage; return }
    challengeTitle.value = challenge.data?.title ?? challengeTitle.value
    access.value = result.data.access ?? null
    items.value = (result.data.items ?? []).filter(x => !!x.publishedVersionId)
    if (content.value && selected.value?.publishedVersionId !== content.value.versionId) clearContent()
    if (!mine.value && !selectedId.value && items.value[0]?.id)
      await router.replace({ path: challengeWriteUpPath(competitionId, challengeId, items.value[0].id), query: route.query })
  }
  async function select(id: string) { if (!id || id === selectedId.value) return; clearContent(); await router.push({ path: challengeWriteUpPath(competitionId, challengeId, id), query: route.query }) }
  async function showContent(versionId: string, request: number) {
    const result = await getChallengeWriteUpContent({ path: { competitionId, competitionChallengeId: challengeId, versionId }, query: { staff: false } })
    if (request !== readSequence) return
    if (result.error || !result.data) { contentError.value = parseApiError(result.error, message('challengeWriteUp.readFailed')).displayMessage; return }
    content.value = result.data
    if (result.data.format === 'Pdf') {
      const grant = await prepareChallengeWriteUpBrowserAccess({ path: { competitionId, competitionChallengeId: challengeId, versionId }, body: { staff: false } })
      if (request !== readSequence) return
      if (grant.error || !grant.data?.previewUrl) contentError.value = parseApiError(grant.error, message('challengeWriteUp.readFailed')).displayMessage
      else { pdfUrl.value = grant.data.previewUrl; pdfKey.value++ }
    }
  }
  async function read() {
    const versionId = selected.value?.publishedVersionId
    if (!versionId || reading.value) return
    const request = ++readSequence; reading.value = true; contentError.value = null
    const result = await getChallengeWriteUpQuote({ path: { competitionId, competitionChallengeId: challengeId, versionId } })
    if (request !== readSequence) return
    if (result.error || !result.data) { contentError.value = parseApiError(result.error, message('challengeWriteUp.readFailed')).displayMessage; reading.value = false; return }
    quote.value = result.data
    if (needsWriteUpConfirmation(result.data)) {
      if (!result.data.canUnlock) contentError.value = message('challengeWriteUp.paused')
      else confirmOpen.value = true
    }
    else await showContent(versionId, request)
    if (request === readSequence) reading.value = false
  }
  async function confirm() {
    const versionId = quote.value?.versionId, policyStamp = quote.value?.policyStamp
    if (!versionId || !policyStamp || confirmPending.value) return
    if (versionId !== selected.value?.publishedVersionId) { confirmOpen.value = false; toast.error(message('challengeWriteUp.changed')); await read(); return }
    confirmPending.value = true; const request = readSequence
    const result = await unlockChallengeWriteUp({ path: { competitionId, competitionChallengeId: challengeId, versionId }, body: { policyStamp } })
    confirmPending.value = false
    if (request !== readSequence) return
    if (result.error || !result.data) { confirmOpen.value = false; contentError.value = parseApiError(result.error, message('challengeWriteUp.readFailed')).displayMessage; await load(); return }
    if (access.value) access.value = { ...access.value, isUnlocked: true, deductionPercent: result.data.deductionPercent }
    confirmOpen.value = false; reading.value = true
    await showContent(versionId, request)
    void ctx.refreshStanding()
    if (request === readSequence) reading.value = false
  }
  function setConfirmOpen(open: boolean) { if (!confirmPending.value) confirmOpen.value = open }
  async function download() {
    if (!content.value?.versionId || downloading.value) return
    downloading.value = true
    const result = await prepareChallengeWriteUpBrowserAccess({ path: { competitionId, competitionChallengeId: challengeId, versionId: content.value.versionId }, body: { staff: false } })
    downloading.value = false
    if (!result.data?.downloadUrl) { toast.error(parseApiError(result.error, message('challengeWriteUp.readFailed')).displayMessage); return }
    startWriteUpBrowserDownload(result.data.downloadUrl)
  }
  function back() { void router.push({ path: `/competitions/${competitionId}/challenges/${challengeId}`, query: route.query }) }
  function openMine() { clearContent(); void router.push({ path: `${challengeWriteUpPath(competitionId, challengeId)}/mine`, query: route.query }) }
  function openPublic() { void router.push({ path: challengeWriteUpPath(competitionId, challengeId), query: route.query }) }
  watch(selectedId, clearContent)
  onMounted(() => { void load(); unwatch = watchCompetition(competitionId, { competitionEventChanged: event => {
    if (['ChallengeWriteUpPublished', 'ChallengeWriteUpWithdrawn', 'ChallengeWriteUpUnlocked', 'CompetitionUpdated', 'ChallengeUpdated'].includes(event.kind)) void load()
  }, onReconnected: () => void load() }) })
  onBeforeUnmount(() => { loadSequence++; readSequence++; unwatch?.() })
  return { ArrowLeft, BookOpen, Download, RefreshCw, competitionId, challengeId, selectedId, selected, items, access, options,
    loading, reading, downloading, confirmPending, error, contentError, content, pdfUrl, pdfKey, quote, confirmOpen, search, mine,
    disabled, mode, title, own, retained, narrow, benefit, load, select, read, confirm, setConfirmOpen, download, back, openMine, openPublic,
    CompetitionParticipantWorkspace: computed(() => mine.value ? markRaw(CompetitionParticipantWorkspaceComponent) : 'section'), Editor: markRaw(ChallengeWriteUpEditorComponent) }
}
export type ChallengeWriteUpPageState = import('vue').ShallowUnwrapRef<ReturnType<typeof useChallengeWriteUpPage>>
