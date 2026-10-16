import { liveSoloMatchListLocation, liveSoloBasePath, liveSoloManagementPath, isLiveSoloManagementPath } from './navigation'
import { computed, inject, markRaw, onMounted, onScopeDispose, ref, shallowRef, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { getLiveSoloWorkspace, getLiveSoloMedia, getLiveSoloPlayerPolicy, getMyTeamEndpoint,
  prepareLiveSoloMedia, lockLiveSoloRoster, confirmLiveSoloReady, prepareLiveSoloRound, startLiveSoloCountdown,
  adjudicateLiveSoloMatch, listLiveSoloQuestionGroupCandidates, listLiveSoloQuestionGroups, listLiveSoloQuestionPool,
  retryLiveSoloPreparation, specifyPreparedLiveSoloGroup, userProfileGet } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse as Round,
  NoCtfapiEndpointsLiveSoloLiveSoloMediaResponse as Media, NoCtfapiEndpointsLiveSoloLiveSoloPlayerPolicyResponse as Configuration,
  NoCtfapiEndpointsTeamsTeamResponse as Team, NoCtfDomainLiveSoloLiveSoloJudgeAction as JudgeAction,
  NoCtfapiEndpointsLiveSoloAdjudicateLiveSoloMatchRequest as JudgeRequest } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { matchStateKey, canLockLiveSoloRoster, roundRemainingSeconds, formatRoundClock } from './live-solo-state'
import LiveSoloQuestions from './LiveSoloQuestions.vue'
import { useLiveSoloScreen } from './media/useLiveSoloScreen'
import { useLiveSoloHub } from './useLiveSoloHub'
import CompetitionParticipantWorkspace from '~/features/competition/CompetitionParticipantWorkspace.vue'
import LiveSoloProgramControl from './LiveSoloProgramControl.vue'
import LiveSoloReview from './LiveSoloReview.vue'
import { indexedDbResultPromptStore, type ResultPromptStore } from './result-observation'
import { resultPresentation } from './result-presentation'

export function useLiveSoloMatch() {
  const route = useRoute(), router = useRouter(), { user } = useAuth()
  const ctx = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), matchId = computed(() => route.params.matchId as string)
  const match = ref<Match | null>(null), round = ref<Round | null>(null), media = ref<Media | null>(null)
  const workspace = ref<import('~/api').NoCtfapiEndpointsLiveSoloLiveSoloWorkspaceResponse | null>(null)
  const workspaceIdentity = computed(() => `${user.value?.userId ?? ''}:${competitionId.value}:${matchId.value}:${isLiveSoloManagementPath(route.path)}`)
  const workspaceActor = ref(''), workspaceFresh = ref(false)
  const workspaceAuthorized = computed(() => workspaceFresh.value && workspaceActor.value === workspaceIdentity.value && workspace.value !== null)
  const configuration = ref<Configuration | null>(null), team = ref<Team | null>(null)
  const loading = ref(true), busy = ref(false), error = ref<UiMessage | null>(null)
  const candidates = ref<import('~/api').NoCtfapiEndpointsLiveSoloLiveSoloQuestionCandidateResponse[]>([]), selectedGroupId = ref<string | null>(null)
  const selectedCandidate = computed(() => candidates.value.find(x => x.id === selectedGroupId.value) ?? null)
  const currentCandidate = computed(() => candidates.value.find(x => x.id === round.value?.questionGroupId) ?? null)
  const groupOptions = computed(() => [{ value: 'Automatic', label: translate('liveSolo.selection.automatic') }, ...candidates.value.filter(x => x.available && x.id).map(x => ({ value: x.id!, label: x.name ?? '—' }))])
  const groupChoice = computed(() => selectedGroupId.value ?? 'Automatic')
  const riskKeys = { SeenByTeam: 'liveSolo.selection.risk.SeenByTeam', SeenByRosterMember: 'liveSolo.selection.risk.SeenByRosterMember',
    SameSource: 'liveSolo.selection.risk.SameSource', PreviouslyUsed: 'liveSolo.selection.risk.PreviouslyUsed', PublicExposure: 'liveSolo.selection.risk.PublicExposure' } as const
  const selectionRisks = computed(() => (selectedCandidate.value?.risks ?? currentCandidate.value?.risks ?? []).map((risk, index) => ({ ...risk,
    key: `${risk.kind}:${risk.competitionChallengeId ?? index}`, label: riskKeys[risk.kind ?? 'PreviouslyUsed'] })))
  const selectionUses = computed(() => selectedCandidate.value?.uses ?? currentCandidate.value?.uses ?? [])
  async function openUsage(id: string) { await router.push(`${liveSoloBasePath(competitionId.value, route.path)}/matches/${id}`) }
  const exclusionKeys = { Empty: 'liveSolo.selection.exclusion.Empty', MissingQuestion: 'liveSolo.selection.exclusion.MissingQuestion',
    DeletedQuestion: 'liveSolo.selection.exclusion.DeletedQuestion', DuplicateQuestion: 'liveSolo.selection.exclusion.DuplicateQuestion',
    InvalidSchedule: 'liveSolo.selection.exclusion.InvalidSchedule', UnsupportedDefinition: 'liveSolo.selection.exclusion.UnsupportedDefinition', ActiveRoundConflict: 'liveSolo.selection.exclusion.ActiveRoundConflict' } as const
  const unavailableCandidates = computed(() => candidates.value.filter(x => !x.available).map(x => ({ ...x, reasons: (x.exclusions ?? []).map(value => ({ value, label: exclusionKeys[value] })) })))
  const candidatePreviewOpen = ref(false), candidatePreviewLoading = ref(false), candidatePreviewTitle = ref('')
  const candidatePreviewItems = ref<import('~/api').NoCtfapiEndpointsLiveSoloLiveSoloPoolQuestionResponse[]>([])
  let candidatePreviewRequest = 0
  function setCandidatePreviewOpen(open: boolean) { candidatePreviewOpen.value = open; if (!open) { candidatePreviewRequest++; candidatePreviewItems.value = [] } }
  async function previewCandidate() {
    if (!staff.value) return
    const choice = selectedCandidate.value ?? currentCandidate.value ?? candidates.value.find(x => x.automaticEligible && !x.reserve) ?? candidates.value.find(x => x.automaticEligible)
    if (!choice?.id) return
    const id = ++candidatePreviewRequest, actor = user.value?.userId
    candidatePreviewOpen.value = true; candidatePreviewLoading.value = true; candidatePreviewItems.value = []; candidatePreviewTitle.value = choice.name ?? ''
    try {
      const path = { competitionId: competitionId.value }
      const [groups, pool] = await Promise.all([listLiveSoloQuestionGroups({ path }), listLiveSoloQuestionPool({ path })])
      if (disposed || id !== candidatePreviewRequest || actor !== user.value?.userId || !staff.value) return
      if (groups.error || pool.error) throw parseLiveSoloError(groups.error ?? pool.error, message('liveSolo.error.load'))
      const group = groups.data?.items?.find(x => x.id === choice.id)
      if (!group) throw parseLiveSoloError(null, message('api.liveSolo.selectionChanged'))
      candidatePreviewItems.value = (group.questions ?? []).map(x => pool.data?.items?.find(q => q.id === x.competitionChallengeId)).filter((x): x is import('~/api').NoCtfapiEndpointsLiveSoloLiveSoloPoolQuestionResponse => !!x)
    } catch (cause) { if (!disposed && id === candidatePreviewRequest) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { if (id === candidatePreviewRequest) candidatePreviewLoading.value = false }
  }
  const startAllowed = computed(() => workspaceAuthorized.value && workspace.value?.allowedOperations?.includes('StartCountdown') === true)
  const preparationKey = computed(() => round.value?.resourceReadiness === 'Failed' ? 'liveSolo.preparation.failed' : round.value?.resourceReadiness === 'Ready'
    ? round.value.environmentRequired === false ? 'liveSolo.preparation.notRequired' : 'liveSolo.preparation.ready' : 'liveSolo.preparation.pending')
  const preparationFailures = { None: null, NotFound: 'api.liveSolo.notFound', ResourceCapacityExceeded: 'api.liveSolo.resourceCapacityExceeded',
    UnsupportedEnvironment: 'api.liveSolo.unsupportedEnvironment', IsolationNotConfigured: 'api.liveSolo.isolationNotConfigured',
    IsolationVerificationFailed: 'api.liveSolo.isolationVerificationFailed', DependencyUnavailable: 'api.liveSolo.dependencyUnavailable' } as const
  const preparationFailure = computed(() => { const key = preparationFailures[round.value?.preparationFailure ?? 'None']; return key ? message(key) : null })
  const selectedRoster = ref<string[]>([]), memberNames = ref<ReadonlyMap<string, string>>(new Map())
  const hasStaffAccess = computed(() => ctx?.competition.value?.administrationRole != null)
  const staff = computed(() => isLiveSoloManagementPath(route.path) && hasStaffAccess.value)
  async function manage() { await router.push(`${liveSoloManagementPath(competitionId.value)}/matches/${matchId.value}`) }
  const backLabel = computed(() => staff.value ? 'liveSolo.navigation.backToMatches' as const : 'liveSolo.back' as const)
  const staffOnlyMedia = computed(() => ctx?.competition.value?.accessMode === 'StaffOnly')
  useLiveSoloHub(competitionId, matchId, staff, load)
  const judge = computed(() => staff.value && workspaceAuthorized.value && workspace.value?.allowedOperations?.some(value => ['PrepareRound', 'StartCountdown', 'RetryPreparation', 'Pause', 'Resume', 'VoidRound', 'ForfeitMatch', 'ManageQuestionPool'].includes(value)) === true && !match.value?.pendingCorrectionId)
  const canCorrect = computed(() => staff.value && workspaceAuthorized.value && workspace.value?.allowedOperations?.includes('ManageQuestionPool') === true
    && (match.value?.state === 'Completed' || !!match.value?.pendingCorrectionId))
  async function correct() {
    const source = match.value?.pendingCorrectionMatchId ?? matchId.value
    const suffix = match.value?.pendingCorrectionId ? '/' + match.value.pendingCorrectionId : ''
    await router.push(liveSoloManagementPath(competitionId.value) + '/corrections/' + source + suffix)
  }
  const myRoster = computed(() => match.value?.rosters?.find(x => x.teamId === team.value?.id) ?? null)
  const captain = computed(() => user.value?.userId != null && team.value?.captainId === user.value.userId)
  const onRoster = computed(() => !!user.value?.userId && myRoster.value?.userIds?.includes(user.value.userId) === true)
  const mayReview = computed(() => staff.value || onRoster.value || ctx?.competition.value?.status === 'Finished')
  const canLock = computed(() => !match.value?.pendingCorrectionId && canLockLiveSoloRoster(match.value, team.value?.id, captain.value, myRoster.value?.locked))
  const allLocked = computed(() => match.value?.rosters?.length === 2 && match.value.rosters.every(x => x.locked))
  const platformStreaming = computed(() => round.value?.platformStreamingEnabled ?? configuration.value?.platformStreamingEnabled ?? false)
  const readOnlyMedia = computed(() => !workspaceAuthorized.value || staff.value && !onRoster.value)
  const screen = useLiveSoloScreen(competitionId, matchId, media, readOnlyMedia)
  const myScreen = computed(() => media.value?.members?.find(x => x.userId === user.value?.userId))
  const screens = computed(() => (media.value?.members ?? []).map((member, index) => ({
    key: member.identity ?? member.userId ?? `${index}`, name: member.userName || memberNames.value.get(member.userId ?? '') || '—',
    teamName: member.side === 'Left' ? match.value?.leftTeamName : match.value?.rightTeamName,
    stateKey: member.state === 'Sharing' ? 'liveSolo.screen.sharing' as const : member.state === 'Connected'
      ? 'liveSolo.screen.confirming' as const : 'liveSolo.screen.waiting' as const,
    stream: member.userId === user.value?.userId ? screen.localStream.value : screen.remoteStreams.value.get(member.identity ?? '') ?? null,
    mayView: staff.value || member.userId === user.value?.userId || media.value?.participantsMayViewOpponents === true,
  })))
  const rosterOptions = computed(() => (team.value?.memberIds ?? []).map(id => ({ id, name: memberNames.value.get(id) ?? '—', selected: selectedRoster.value.includes(id) })))
  const leftReady = computed(() => match.value?.rosters?.find(x => x.teamId === match.value?.leftTeamId)?.ready === true)
  const rightReady = computed(() => match.value?.rosters?.find(x => x.teamId === match.value?.rightTeamId)?.ready === true)
  const judgeOpen = ref(false), judgeAction = ref<JudgeAction>('Pause'), judgeReason = ref(''), forfeitingTeam = ref<string | null>(null)
  const decision = shallowRef<JudgeRequest | null>(null)
  const leaveOpen = ref(false)
  let leaveTo: string | null = null, allowLeave = false
  let disposed = false, request = 0, pending = false, refreshQueued = false, timer: ReturnType<typeof setTimeout> | undefined, clockTimer: ReturnType<typeof setInterval> | undefined
  const elapsedSinceSnapshot = ref(0)
  let observedAt = 0
  const clock = computed(() => formatRoundClock(roundRemainingSeconds(round.value, elapsedSinceSnapshot.value)))

  async function load() {
    if (disposed) return
    if (pending) { refreshQueued = true; return }
    pending = true; const id = ++request, identity = workspaceIdentity.value; let authorized = false
    try {
      if (!ctx?.competition.value) await ctx?.refresh()
      const path = { competitionId: competitionId.value, matchId: matchId.value }
      const [result, config, mine] = await Promise.all([getLiveSoloWorkspace({ path, query: { staff: staff.value } }),
        getLiveSoloPlayerPolicy({ path: { competitionId: path.competitionId } }),
        getMyTeamEndpoint({ path: { competitionId: path.competitionId } })])
      if (disposed || id !== request || identity !== workspaceIdentity.value) return
      if ([401, 403, 404].includes(result.response?.status ?? 0)) { workspace.value = null; match.value = null; round.value = null }
      if (result.error || !result.data?.match || config.error || !config.data) throw parseLiveSoloError(result.error ?? config.error, message('liveSolo.error.load'))
      workspace.value = result.data; match.value = result.data.match; round.value = result.data.round ?? null
      workspaceActor.value = identity; workspaceFresh.value = true; authorized = true
      configuration.value = config.data; team.value = mine.data ?? null
      observedAt = performance.now(); elapsedSinceSnapshot.value = 0
      await observeResults()
      if (platformStreaming.value) {
        const room = await getLiveSoloMedia({ path })
        if (disposed || id !== request || identity !== workspaceIdentity.value) return
        if (room.response?.status === 404) media.value = null
        else if (room.error) throw parseLiveSoloError(room.error, message('liveSolo.error.media'))
        else media.value = room.data ?? null
      }
      else { media.value = null; await screen.disconnect() }
      const names = new Map(memberNames.value)
      if (user.value?.userId) names.set(user.value.userId, user.value.userName ?? '—')
      const missing = (mine.data?.memberIds ?? []).filter(member => !names.has(member))
      const profiles = await Promise.all(missing.map(userId => userProfileGet({ path: { userId } })))
      if (disposed || id !== request || identity !== workspaceIdentity.value) return
      profiles.forEach((profile, index) => { if (profile.data?.userName) names.set(missing[index]!, profile.data.userName) })
      memberNames.value = names
      if (myRoster.value?.locked) selectedRoster.value = [...(myRoster.value.userIds ?? [])]
      else selectedRoster.value = selectedRoster.value.filter(member => mine.data?.memberIds?.includes(member))
      if (staff.value) {
        const lists = await listLiveSoloQuestionGroupCandidates({ path })
        if (disposed || id !== request || identity !== workspaceIdentity.value) return
        if (lists.error || !lists.data) throw parseLiveSoloError(lists.error, message('liveSolo.error.load'))
        candidates.value = lists.data.items ?? []
      } else candidates.value = []
      error.value = null
    }
    catch (cause) {
      if (!disposed && id === request && identity === workspaceIdentity.value) {
        if (!authorized) workspaceFresh.value = false
        workspace.value = null; match.value = null; round.value = null; media.value = null; candidates.value = []
        candidatePreviewRequest++; candidatePreviewOpen.value = false; candidatePreviewItems.value = []
        resultOpen.value = false; resultWindow.value = null
        error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage; await screen.disconnect()
      }
    }
    finally { pending = false; loading.value = false; if (refreshQueued && !disposed) { refreshQueued = false; void load() } }
  }
  async function operation(work: () => PromiseLike<{ error?: unknown; response?: Response }>) {
    if (busy.value || !workspaceAuthorized.value) return
    busy.value = true; error.value = null
    let failure: UiMessage | null = null
    try { const result = await work(); if (result.error || result.response?.ok === false) throw parseLiveSoloError(result.error, message('liveSolo.error.operation')) }
    catch (cause) { failure = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false; await load(); if (failure) error.value = failure }
  }
  function toggleRoster(id: string, checked: boolean | 'indeterminate') {
    if (!canLock.value) return
    if (checked === true && !selectedRoster.value.includes(id)) selectedRoster.value = [...selectedRoster.value, id]
    else if (checked !== true) selectedRoster.value = selectedRoster.value.filter(x => x !== id)
  }
  async function lockRoster() {
    if (!canLock.value || !team.value?.id || !match.value?.concurrencyStamp || !selectedRoster.value.length
      || selectedRoster.value.length > (configuration.value?.maximumRosterMembers ?? 1)) return
    await operation(() => lockLiveSoloRoster({ path: { competitionId: competitionId.value, matchId: matchId.value },
      body: { teamId: team.value!.id!, expectedStamp: match.value!.concurrencyStamp!, userIds: [...selectedRoster.value] } }))
  }
  async function ready() {
    if (!onRoster.value || !myRoster.value?.locked || !match.value?.concurrencyStamp) return
    await operation(() => confirmLiveSoloReady({ path: { competitionId: competitionId.value, matchId: matchId.value }, body: { expectedStamp: match.value!.concurrencyStamp! } }))
  }
  async function prepareMedia() {
    if (!platformStreaming.value || !allLocked.value || (!judge.value && !onRoster.value) || !match.value?.concurrencyStamp) return
    await operation(() => prepareLiveSoloMedia({ path: { competitionId: competitionId.value, matchId: matchId.value }, body: { expectedMatchStamp: match.value!.concurrencyStamp! } }))
  }
  function chooseGroup(id: string) { if (!busy.value && judge.value) selectedGroupId.value = id === 'Automatic' ? null : id }
  async function retryPreparation(questionId?: string) {
    if (!judge.value || !round.value?.id || !round.value.concurrencyStamp) return
    await operation(() => retryLiveSoloPreparation({ path: { competitionId: competitionId.value, matchId: matchId.value, roundId: round.value!.id! }, body: { expectedStamp: round.value!.concurrencyStamp!, questionId } }))
  }
  async function specifyCurrent() {
    const candidate = currentCandidate.value
    if (!judge.value || !round.value?.id || !round.value.concurrencyStamp || !candidate?.id || !candidate.concurrencyStamp || !candidate.selectionRevision) return
    await operation(() => specifyPreparedLiveSoloGroup({ path: { competitionId: competitionId.value, matchId: matchId.value, roundId: round.value!.id! }, body: { expectedStamp: round.value!.concurrencyStamp!, questionGroupId: candidate.id!, expectedGroupStamp: candidate.concurrencyStamp!, selectionRevision: candidate.selectionRevision! } }))
  }
  async function prepareRound() {
    if (!judge.value || !match.value?.concurrencyStamp) return
    await operation(() => prepareLiveSoloRound({ path: { competitionId: competitionId.value, matchId: matchId.value }, body: { expectedStamp: match.value!.concurrencyStamp!, questionGroupId: selectedGroupId.value,
        expectedGroupStamp: selectedCandidate.value?.concurrencyStamp ?? null, selectionRevision: selectedCandidate.value?.selectionRevision ?? null } }))
  }
  async function start() {
    if (!judge.value || !round.value?.id || !round.value.concurrencyStamp) return
    await operation(() => startLiveSoloCountdown({ path: { competitionId: competitionId.value, matchId: matchId.value, roundId: round.value!.id! }, body: { expectedStamp: round.value!.concurrencyStamp! } }))
  }
  function openDecision(action: JudgeAction) {
    if (!judge.value || !match.value?.concurrencyStamp) return
    judgeAction.value = action; judgeReason.value = ''; forfeitingTeam.value = null
    decision.value = { action, reason: '', expectedMatchStamp: match.value.concurrencyStamp,
      expectedRoundId: round.value?.id ?? null, expectedRoundStamp: round.value?.concurrencyStamp ?? null, expectedTimelineRevision: round.value?.timelineRevision ?? null }
    judgeOpen.value = true
  }
  async function confirmDecision() {
    if (!decision.value || busy.value) return
    if (!judgeReason.value.trim() || judgeAction.value === 'ForfeitMatch' && !forfeitingTeam.value) { error.value = message('liveSolo.judge.reasonRequired'); return }
    const input = { ...decision.value, reason: judgeReason.value.trim(), forfeitingTeamId: forfeitingTeam.value }
    judgeOpen.value = false
    await operation(() => adjudicateLiveSoloMatch({ path: { competitionId: competitionId.value, matchId: matchId.value }, body: input }))
  }
  function setJudgeOpen(open: boolean) { judgeOpen.value = open; if (!open) void observeResults() }
  function setLeaveOpen(open: boolean) { leaveOpen.value = open; if (!open) leaveTo = null }
  async function confirmLeave() {
    const target = leaveTo; leaveOpen.value = false; leaveTo = null
    await screen.disconnect(); allowLeave = true
    if (target) await router.push(target)
  }
  function beforeUnload(event: BeforeUnloadEvent) { if (screen.publishing.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(to => {
    if (!screen.publishing.value || allowLeave) return true
    leaveTo = to.fullPath; leaveOpen.value = true; return false
  })
  const workspaceTab = ref('Current')
  const workspacePanel = ref('Play')
  function setWorkspacePanel(value: string | number) { workspacePanel.value = String(value) }
  const resultButtonId = computed(() => `live-solo-result-${matchId.value}`)
  const resultOpen = ref(false), resultWindow = shallowRef<ReturnType<typeof resultPresentation>>(null)
  let promptStore: ResultPromptStore | null = null, promptStorageFailed = false
  const stableResult = computed(() => match.value ? resultPresentation(match.value, round.value,
    ['Completed', 'Canceled'].includes(match.value.state ?? '') ? 'Match' : 'Round') : null)
  const dirty = computed(() => judgeOpen.value || candidatePreviewOpen.value || busy.value)
  const resultKey = () => `${user.value?.userId}:${competitionId.value}:${matchId.value}`.toLowerCase()
  async function observeResults() {
    const current = match.value, currentRound = round.value, actor = user.value?.userId, key = resultKey()
    if (!actor || !current?.id || promptStorageFailed) return
    try {
      if (typeof indexedDB === 'undefined') { promptStorageFailed = true; return }
      promptStore ??= indexedDbResultPromptStore()
      const prompt = await promptStore.observe(key, {
        matchId: current.id, matchActive: !['Completed', 'Canceled', 'AwaitingOpponents'].includes(current.state ?? ''),
        matchRevision: current.resultRevision ?? 0, matchEnded: ['Completed', 'Canceled'].includes(current.state ?? ''),
        roundId: currentRound?.id ?? null, roundActive: !!currentRound && ['Preparing', 'Countdown', 'Running', 'ConfirmingResult'].includes(currentRound.state ?? ''),
        roundRevision: currentRound?.resultRevision ?? 0, roundEnded: !!currentRound && ['Won', 'TimedOut', 'Canceled'].includes(currentRound.state ?? ''),
        defer: dirty.value || resultOpen.value || !!current.pendingCorrectionId,
      })
      if (prompt && !disposed && key === resultKey()) {
        resultWindow.value = resultPresentation(current, currentRound, prompt); resultOpen.value = !!resultWindow.value
      }
    } catch { promptStorageFailed = true }
  }
  function showResult() { resultWindow.value = stableResult.value; resultOpen.value = !!resultWindow.value }
  function setResultOpen(open: boolean) { resultOpen.value = open; if (!open) void observeResults() }
  async function nextRound() { setResultOpen(false); await prepareRound() }
  function setWorkspaceTab(value: string | number) { workspaceTab.value = String(value) }
  const conditionKeys = {
    CompetitionNotRunning: 'liveSolo.workspace.condition.CompetitionNotRunning', LeftRosterUnlocked: 'liveSolo.workspace.condition.LeftRosterUnlocked',
    RightRosterUnlocked: 'liveSolo.workspace.condition.RightRosterUnlocked', LeftNotReady: 'liveSolo.workspace.condition.LeftNotReady', RightNotReady: 'liveSolo.workspace.condition.RightNotReady',
    NoPreparedRound: 'liveSolo.workspace.condition.NoPreparedRound', EnvironmentPreparing: 'liveSolo.workspace.condition.EnvironmentPreparing',
    EnvironmentFailed: 'liveSolo.workspace.condition.EnvironmentFailed', MediaRequired: 'liveSolo.workspace.condition.MediaRequired',
    SelectionChanged: 'liveSolo.workspace.condition.SelectionChanged', CorrectionPending: 'liveSolo.workspace.condition.CorrectionPending',
  } as const
  const conditions = computed(() => (workspace.value?.missingConditions ?? []).map(value => ({ value, label: conditionKeys[value] })))
  const failedResources = computed(() => (workspace.value?.resources ?? []).filter(x => !x.openedAt && x.readiness === 'Failed').map(x => ({ ...x,
    failureLabel: preparationFailures[x.failure ?? 'None'] })))
  const phaseKey = computed(() => round.value?.state === 'ConfirmingResult' ? 'liveSolo.workspace.confirming' : match.value?.state === 'Completed'
    ? 'liveSolo.workspace.completed' : match.value?.state === 'Canceled' ? 'liveSolo.workspace.canceled' : round.value?.state === 'Countdown'
      ? 'liveSolo.workspace.countdown' : round.value?.state === 'Won' || round.value?.state === 'TimedOut' || round.value?.state === 'Canceled'
        ? 'liveSolo.workspace.betweenRounds' : 'liveSolo.workspace.preparing')
  const leftAvatar = computed(() => match.value?.leftAvatarUrl ?? null)
  const rightAvatar = computed(() => match.value?.rightAvatarUrl ?? null)
  const resultAvatar = computed(() => resultWindow.value?.winnerAvatar === match.value?.leftTeamId ? leftAvatar.value : resultWindow.value?.winnerAvatar === match.value?.rightTeamId ? rightAvatar.value : null)
  watch(dirty, value => { if (!value) void observeResults() })
  async function back() { await router.push(liveSoloMatchListLocation(competitionId.value, route.path, matchId.value)) }
  async function tick() { await load(); if (!disposed) timer = setTimeout(tick, 5000) }
  onMounted(() => {
    void tick(); window.addEventListener('beforeunload', beforeUnload)
    clockTimer = setInterval(() => { elapsedSinceSnapshot.value = performance.now() - observedAt }, 250)
  })
  watch([() => user.value?.userId, competitionId, matchId, staff], () => {
    request++; workspaceFresh.value = false; workspaceActor.value = ''; workspace.value = null; match.value = null; round.value = null; media.value = null; team.value = null
    candidates.value = []; selectedRoster.value = []; memberNames.value = new Map(); resultOpen.value = false; resultWindow.value = null
    candidatePreviewRequest++; candidatePreviewOpen.value = false; candidatePreviewItems.value = []
    void screen.disconnect(); void load()
  })
  onScopeDispose(() => {
    disposed = true; request++; candidatePreviewRequest++; if (timer) clearTimeout(timer); if (clockTimer) clearInterval(clockTimer)
    window.removeEventListener('beforeunload', beforeUnload)
  })
  return { competitionId, matchId, match, round, media, workspace, workspaceTab, setWorkspaceTab, workspacePanel, setWorkspacePanel, resultButtonId, conditions, phaseKey, leftAvatar, rightAvatar,
    stableResult, resultWindow, resultOpen, setResultOpen, showResult, nextRound, resultAvatar, leftReady, rightReady, failedResources,
    candidatePreviewOpen, setCandidatePreviewOpen, candidatePreviewLoading, candidatePreviewTitle, candidatePreviewItems, previewCandidate, Review: markRaw(LiveSoloReview),
    platformStreaming, configuration, loading, busy, team, myRoster, captain, onRoster, mayReview, staffOnlyMedia,
    canLock, allLocked, rosterOptions, selectedRoster, toggleRoster, lockRoster, ready, prepareMedia, prepareRound, start,
    candidates, groupOptions, groupChoice, chooseGroup, currentCandidate, selectionRisks, selectionUses, openUsage, unavailableCandidates, startAllowed, preparationKey, preparationFailure, retryPreparation, specifyCurrent,
    staff, hasStaffAccess, manage, backLabel, readOnlyMedia, judge, canCorrect, correct, screens, myScreen, clock, stateKey: computed(() => loading.value && !match.value ? 'liveSolo.workspace.loading' as const : matchStateKey(match.value?.state)), back, load,
    judgeOpen, judgeAction, judgeReason, forfeitingTeam, openDecision, confirmDecision, setJudgeOpen,
    leaveOpen, setLeaveOpen, confirmLeave, Workspace: markRaw(CompetitionParticipantWorkspace),
    ProgramControl: markRaw(LiveSoloProgramControl), Questions: markRaw(LiveSoloQuestions), canSubmit: computed(() => workspaceAuthorized.value && workspace.value?.allowedOperations?.includes('SubmitFlag') === true),
    flagDock: computed(() => `live-solo-flag-${matchId.value}`),
    questionsVisible: computed(() => onRoster.value && !!round.value?.startedAt && ['Running', 'ConfirmingResult'].includes(round.value.state ?? '')),
    ...screen, mediaError: computed(() => platformStreaming.value ? screen.error.value : null), error }
}
export type LiveSoloMatchState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloMatch>>
