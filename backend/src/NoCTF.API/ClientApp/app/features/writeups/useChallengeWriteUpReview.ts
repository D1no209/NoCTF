import { markRaw } from 'vue'
import { useMediaQuery } from '@vueuse/core'
import { adminListChallengeWriteUpReviews, adminListCompetitionChallenges, getChallengeWriteUpContent,
  prepareChallengeWriteUpBrowserAccess, reviewChallengeWriteUp, createTeamWriteUpConsultation, getChallengeWriteUpSettings, listChallengeWriteUps, getCompetitionEndpoint } from '~/api'
import type { NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpReviewFilterProtocol, NoCtfDomainChallengesWriteUpsWriteUpSource,
  NoCtfDomainChallengesWriteUpsWriteUpReviewAction, NoCtfApplicationChallengesWriteUpsWriteUpSettingsView, NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'
import { message } from '~/utils/i18n'
import type { UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'
import SingleWriteUpSettingsComponent from './SingleWriteUpSettings.vue'
import ChallengeWriteUpEditorComponent from './ChallengeWriteUpEditor.vue'
import { latestWriteUpVersion, writeUpPublicationVersion, writeUpPublicationLabel, writeUpStatusKey } from './writeup-state'
import type { WriteUp, WriteUpContent } from './writeup-state'
import { matchesWriteUpTarget, publishWriteUpVersion, writeUpActionTarget } from './writeup-publication'
import type { WriteUpActionTarget } from './writeup-publication'

export function useChallengeWriteUpReview(props: Readonly<{ competitionId: string }>) {
  const route = useRoute(), router = useRouter()
  const narrow = useMediaQuery('(max-width: 1279px)'), panel = ref('list')
  const rows = ref<WriteUp[]>([]), selected = ref<WriteUp | null>(null)
  const loading = ref(true), previewLoading = ref(false), pending = ref(false)
  const error = ref<UiMessage | null>(null), previewError = ref<UiMessage | null>(null)
  const search = ref(String(route.query.reviewSearch ?? ''))
  const initialFilter = String(route.query.reviewStatus ?? 'Submitted')
  const filter = ref<NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpReviewFilterProtocol>(['All', 'Submitted', 'Published', 'Rejected', 'Draft'].includes(initialFilter) ? initialFilter as NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpReviewFilterProtocol : 'Submitted')
  const initialSource = String(route.query.reviewSource ?? 'All')
  const initialPage = Number(route.query.reviewPage ?? 1)
  const source = ref(['All', 'Team', 'Official'].includes(initialSource) ? initialSource : 'All'), page = ref(Number.isSafeInteger(initialPage) && initialPage > 0 ? initialPage : 1), limit = ref(20), total = ref(0)
  const canManage = ref(false), canJudge = ref(false), display = ref('submitted')
  const submittedContent = ref<WriteUpContent | null>(null), publishedContent = ref<WriteUpContent | null>(null)
  const submittedPdf = ref<string | null>(null), publishedPdf = ref<string | null>(null)
  const reason = ref(''), confirmation = ref<NoCtfDomainChallengesWriteUpsWriteUpReviewAction | null>(null)
  const confirmationTarget = ref<WriteUpActionTarget | null>(null), publicationNotice = ref(false)
  const competitionStatus = ref<NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol | null>(null)
  const settingsOpen = ref(false), officialOpen = ref(false), officialChallengeId = ref('')
  const settings = ref<NoCtfApplicationChallengesWriteUpsWriteUpSettingsView | null>(null), confirmationSettings = ref<NoCtfApplicationChallengesWriteUpsWriteUpSettingsView | null>(null)
  let officialEditor: { confirmDiscard: () => Promise<boolean> } | null = null
  const challengeOptions = ref<{ value: string; label: string }[]>([])
  const pageCount = computed(() => Math.max(1, Math.ceil(total.value / limit.value)))
  const selectedId = computed(() => selected.value?.id ?? null)
  const selectedVersion = computed(() => latestWriteUpVersion(selected.value))
  const publishedVersion = computed(() => selected.value?.published)
  const publishVersion = computed(() => display.value === 'published' ? null : writeUpPublicationVersion(selected.value, selectedVersion.value))
  const publicationLabel = computed(() => writeUpPublicationLabel(publishVersion.value))
  const enabled = computed(() => settings.value?.enabled === true)
  const canPublish = computed(() => canManage.value && enabled.value && !!publishVersion.value && !pending.value && !loading.value && !previewLoading.value && !previewError.value)
  const canReject = computed(() => canJudge.value && enabled.value && display.value !== 'published' && selectedVersion.value?.state === 'Submitted' && !pending.value)
  const publicationBlocked = computed(() => !enabled.value ? 'challengeWriteUp.disabled'
    : display.value === 'published' ? 'challengeWriteUp.selectCurrentVersion'
      : selectedVersion.value?.state === 'Draft' && selected.value?.source !== 'Official' ? 'challengeWriteUp.teamDraftNotPublishable'
        : !publishVersion.value ? 'challengeWriteUp.notPublishable' : null)
  const statusKey = computed(() => writeUpStatusKey(selected.value))
  const options = computed(() => rows.value.map(row => ({ value: row.id ?? '', label: `${row.challengeTitle ?? ''} · ${row.source === 'Official' ? translate('challengeWriteUp.official') : row.authorName ?? ''}`,
    row, statusKey: writeUpStatusKey(row) })))
  let loadSequence = 0, previewSequence = 0, unwatch: (() => void) | undefined, searchTimer: ReturnType<typeof setTimeout> | undefined
  async function load() {
    if (pending.value) return
    const request = ++loadSequence; loading.value = rows.value.length === 0; error.value = null
    const [result, policy] = await Promise.all([adminListChallengeWriteUpReviews({ path: { competitionId: props.competitionId }, query: { filter: filter.value,
      source: source.value === 'All' ? undefined : source.value as NoCtfDomainChallengesWriteUpsWriteUpSource,
      search: search.value.trim() || undefined, offset: (page.value - 1) * limit.value, limit: limit.value } }), getChallengeWriteUpSettings({ path: { competitionId: props.competitionId }, query: {} })])
    if (request !== loadSequence) return
    loading.value = false
    if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.loadFailed')).displayMessage; return }
    rows.value = result.data.items ?? []; total.value = result.data.totalCount ?? 0
    settings.value = policy.data?.settings ?? null
    canManage.value = result.data.canManage ?? false; canJudge.value = result.data.canJudge ?? false
    let current = rows.value.find(row => row.id === selected.value?.id)
    if (!current && selected.value?.competitionChallengeId) {
      const result = await listChallengeWriteUps({ path: { competitionId: props.competitionId, competitionChallengeId: selected.value.competitionChallengeId }, query: { staff: true } })
      if (request !== loadSequence) return
      current = result.data?.items?.find(row => row.id === selected.value?.id)
    }
    if (current) {
      const previousIds = [selectedVersion.value?.id, publishedVersion.value?.id].join(':')
      selected.value = current
      if (previousIds !== [selectedVersion.value?.id, publishedVersion.value?.id].join(':')) await preview()
    }
    if (!selected.value) {
      const id = typeof route.query.review === 'string' ? route.query.review : rows.value[0]?.id
      if (id) await select(id, false)
    }
  }
  async function readVersion(versionId?: string): Promise<{ content: WriteUpContent | null; pdf: string | null }> {
    if (!versionId || !selected.value?.competitionChallengeId) return { content: null, pdf: null }
    const path = { competitionId: props.competitionId, competitionChallengeId: selected.value.competitionChallengeId, versionId }
    const result = await getChallengeWriteUpContent({ path, query: { staff: true } })
    if (result.error || !result.data) throw result.error
    if (result.data.format !== 'Pdf') return { content: result.data, pdf: null }
    const grant = await prepareChallengeWriteUpBrowserAccess({ path, body: { staff: true } })
    if (grant.error || !grant.data?.previewUrl) throw grant.error
    return { content: result.data, pdf: grant.data.previewUrl }
  }
  async function preview() {
    const request = ++previewSequence; previewLoading.value = true; previewError.value = null
    submittedContent.value = null; publishedContent.value = null; submittedPdf.value = null; publishedPdf.value = null
    try {
      const leftId = selectedVersion.value?.id, rightId = publishedVersion.value?.id
      const [left, right] = await Promise.all([display.value === 'published' ? Promise.resolve(null) : readVersion(leftId),
        display.value === 'submitted' ? Promise.resolve(null) : readVersion(rightId)])
      if (request !== previewSequence) return
      submittedContent.value = left?.content ?? null; submittedPdf.value = left?.pdf ?? null
      publishedContent.value = right?.content ?? null; publishedPdf.value = right?.pdf ?? null
    }
    catch (cause) { if (request === previewSequence) previewError.value = parseApiError(cause, message('challengeWriteUp.readFailed')).displayMessage }
    finally { if (request === previewSequence) previewLoading.value = false }
  }
  async function select(id: string, advance = true) {
    if (pending.value) return
    const row = rows.value.find(item => item.id === id)
    if (!row) return
    selected.value = row; reason.value = ''; display.value = 'submitted'; publicationNotice.value = false
    if (narrow.value && advance) panel.value = 'preview'
    void router.replace({ query: { ...route.query, review: id } })
    await preview()
  }
  function setDisplay(value: unknown) { if (value === 'submitted' || value === 'published' || value === 'compare') display.value = value }
  function setPanel(value: unknown) { if (value === 'list' || selected.value && (value === 'preview' || value === 'actions')) panel.value = value }
  function resetFilter() {
    if (pending.value) return
    page.value = 1; selected.value = null
    void router.replace({ query: { ...route.query, review: undefined, reviewSearch: search.value || undefined,
      reviewStatus: filter.value, reviewSource: source.value, reviewPage: '1' } })
    void load()
  }
  function changePage(value: number) { page.value = value; selected.value = null; void router.replace({ query: { ...route.query, reviewPage: String(value), review: undefined } }); void load() }
  function changeLimit(value: number) { limit.value = value; resetFilter() }
  watch([filter, source], resetFilter)
  watch(search, () => { clearTimeout(searchTimer); searchTimer = setTimeout(resetFilter, 250) })
  watch(display, () => void preview())
  async function review(action: NoCtfDomainChallengesWriteUpsWriteUpReviewAction) {
    if (action === 'Reject' ? !canJudge.value : !canManage.value) return
    const row = selected.value
    const version = action === 'Withdraw' ? publishedVersion.value : action === 'Publish' ? publishVersion.value : row?.submitted
    const target = action === 'Reject' ? writeUpActionTarget(row, version) : confirmationTarget.value
    if (!row || !target || pending.value) return
    if (!matchesWriteUpTarget(row, version, target) || (action === 'Publish' && !canPublish.value) || (action === 'Reject' && !canReject.value)) {
      confirmation.value = null; error.value = message('challengeWriteUp.publicationChanged'); return
    }
    if (action === 'Reject' && !reason.value.trim()) { error.value = message('challengeWriteUp.error.InvalidContent'); return }
    pending.value = true; error.value = null; publicationNotice.value = false; loadSequence++
    try {
      if (action === 'Publish') selected.value = await publishWriteUpVersion(props.competitionId, target, submitted => {
        selected.value = { ...submitted, viewedTeamCount: row.viewedTeamCount }; publicationNotice.value = true
      })
      else {
        const result = await reviewChallengeWriteUp({ path: { competitionId: props.competitionId, competitionChallengeId: target.challengeId, writeUpId: target.writeUpId },
          body: { versionId: target.versionId, expectedStamp: target.expectedStamp, action, reason: action === 'Reject' ? reason.value.trim() : undefined } })
        if (result.error || !result.data) throw result.error
        selected.value = result.data
      }
      selected.value = { ...selected.value, viewedTeamCount: row.viewedTeamCount }; publicationNotice.value = false
      toast.success(message('challengeWriteUp.reviewSaved'))
    }
    catch (cause) { error.value = parseApiError(cause, message('challengeWriteUp.reviewFailed')).displayMessage; return }
    finally { pending.value = false; confirmation.value = null }
    await load(); await preview()
  }
  async function requestPublish() {
    if (!canPublish.value) return
    const target = writeUpActionTarget(selected.value, publishVersion.value)
    if (!target) return
    pending.value = true; loadSequence++
    try {
      const [result, competition] = await Promise.all([getChallengeWriteUpSettings({ path: { competitionId: props.competitionId }, query: { competitionChallengeId: target.challengeId } }), getCompetitionEndpoint({ path: { competitionId: props.competitionId } })])
      if (result.error || !result.data?.settings) { error.value = parseApiError(result.error, message('challengeWriteUp.settingsFailed')).displayMessage; return }
      if (competition.error || !competition.data) { error.value = parseApiError(competition.error, message('challengeWriteUp.loadFailed')).displayMessage; return }
      if (!result.data.settings.enabled) { error.value = message('challengeWriteUp.disabled'); return }
      if (!matchesWriteUpTarget(selected.value, publishVersion.value, target)) { error.value = message('challengeWriteUp.publicationChanged'); return }
      competitionStatus.value = competition.data.status ?? null
      confirmationSettings.value = result.data.settings; confirmationTarget.value = target; confirmation.value = 'Publish'
    }
    catch (cause) { error.value = parseApiError(cause, message('challengeWriteUp.loadFailed')).displayMessage }
    finally { pending.value = false }
  }
  function requestWithdraw() {
    if (!canManage.value || pending.value || !selected.value?.publishedVersionId) return
    confirmationTarget.value = writeUpActionTarget(selected.value, publishedVersion.value)
    if (confirmationTarget.value) confirmation.value = 'Withdraw'
  }
  function setConfirmationOpen(open: boolean) { if (!open && !pending.value) confirmation.value = null }
  function confirmReview() { if (confirmation.value) void review(confirmation.value) }
  function reject() { void review('Reject') }
  async function openOfficial() {
    const result = await adminListCompetitionChallenges({ path: { competitionId: props.competitionId }, query: { includeDeleted: false } })
    if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.loadFailed')).displayMessage; return }
    challengeOptions.value = (result.data.items ?? []).filter(x => x.id && !x.deletedAt).map(x => ({ value: x.id!, label: x.title ?? '' }))
    officialChallengeId.value = selected.value?.competitionChallengeId ?? challengeOptions.value[0]?.value ?? ''
    officialOpen.value = true
  }
  function bindOfficialEditor(value: unknown) { officialEditor = value as typeof officialEditor }
  async function setOfficialOpen(open: boolean) {
    if (!open && officialEditor && !await officialEditor.confirmDiscard()) return
    officialOpen.value = open
  }
  async function changeOfficialChallenge(value: unknown) {
    if (officialEditor && !await officialEditor.confirmDiscard()) return
    officialChallengeId.value = String(value ?? '')
  }
  async function consultation() {
    const row = selected.value
    if (!row?.teamId || !row.competitionChallengeId || !canJudge.value || pending.value) return
    pending.value = true
    const result = await createTeamWriteUpConsultation({ path: { competitionId: props.competitionId, teamId: row.teamId }, body: {
      competitionChallengeId: row.competitionChallengeId, title: translate('challengeWriteUp.consultationTitle', { challenge: row.challengeTitle ?? '' }),
      body: reason.value.trim() || translate('challengeWriteUp.consultationBody'),
    } })
    pending.value = false
    if (result.error || !result.data?.threadRootId) { error.value = parseApiError(result.error, message('challengeWriteUp.reviewFailed')).displayMessage; return }
    toast.success(message('challengeWriteUp.consultationCreated'))
    await router.push({ path: `/competitions/${props.competitionId}/questions`, query: { question: result.data.threadRootId } })
  }
  onMounted(() => { void load(); unwatch = watchCompetition(props.competitionId, { competitionEventChanged: event => {
    if (event.kind.startsWith('ChallengeWriteUp') || event.kind === 'CompetitionUpdated' || event.kind === 'ChallengeUpdated') void load()
  }, onReconnected: () => void load() }) })
  onBeforeUnmount(() => { loadSequence++; previewSequence++; unwatch?.(); clearTimeout(searchTimer) })
  return { rows, selected, loading, previewLoading, pending, error, previewError, search, filter, source, page, limit, total,
    canManage, canJudge, display, submittedContent, publishedContent, submittedPdf, publishedPdf, reason, confirmation,
    settingsOpen, officialOpen, officialChallengeId, challengeOptions, selectedId, selectedVersion, publishedVersion, statusKey,
    pageCount, options, load, select, changePage, changeLimit, requestPublish, requestWithdraw, setConfirmationOpen, confirmReview, reject,
    setDisplay, setPanel, narrow, panel, enabled, publishVersion, canPublish, canReject, publicationLabel, publicationBlocked, publicationNotice, confirmationTarget, confirmationSettings, competitionStatus, bindOfficialEditor, setOfficialOpen, changeOfficialChallenge,
    openOfficial, consultation, competitionId: computed(() => props.competitionId),
    Settings: markRaw(SingleWriteUpSettingsComponent), Editor: markRaw(ChallengeWriteUpEditorComponent) }
}
export type ChallengeWriteUpReviewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useChallengeWriteUpReview>>
