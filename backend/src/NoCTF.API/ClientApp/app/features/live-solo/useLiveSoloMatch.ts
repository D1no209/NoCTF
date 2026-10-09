import { computed, inject, markRaw, onMounted, onScopeDispose, ref, shallowRef, watch } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { getLiveSoloMatch, getLiveSoloMedia, getLiveSoloRound, getLiveSoloConfiguration, getMyTeamEndpoint,
  prepareLiveSoloMedia, lockLiveSoloRoster, confirmLiveSoloReady, prepareLiveSoloRound, startLiveSoloCountdown,
  adjudicateLiveSoloMatch, userProfileGet } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloRoundResponse as Round,
  NoCtfapiEndpointsLiveSoloLiveSoloMediaResponse as Media, NoCtfapiEndpointsLiveSoloLiveSoloConfigurationContract as Configuration,
  NoCtfapiEndpointsTeamsTeamResponse as Team, NoCtfDomainLiveSoloLiveSoloJudgeAction as JudgeAction,
  NoCtfapiEndpointsLiveSoloAdjudicateLiveSoloMatchRequest as JudgeRequest } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { matchStateKey, canJudgeLiveSolo, roundRemainingSeconds, formatRoundClock, canPlayLiveSolo } from './live-solo-state'
import LiveSoloQuestions from './LiveSoloQuestions.vue'
import { useLiveSoloScreen } from './media/useLiveSoloScreen'
import CompetitionParticipantWorkspace from '~/features/competition/CompetitionParticipantWorkspace.vue'

export function useLiveSoloMatch() {
  const route = useRoute(), router = useRouter(), { user } = useAuth()
  const ctx = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), matchId = computed(() => route.params.matchId as string)
  const match = ref<Match | null>(null), round = ref<Round | null>(null), media = ref<Media | null>(null)
  const configuration = ref<Configuration | null>(null), team = ref<Team | null>(null)
  const loading = ref(true), busy = ref(false), error = ref<UiMessage | null>(null)
  const selectedRoster = ref<string[]>([]), memberNames = ref<ReadonlyMap<string, string>>(new Map())
  const staff = computed(() => ctx?.competition.value?.administrationRole != null)
  const judge = computed(() => canJudgeLiveSolo(ctx?.competition.value?.administrationRole))
  const myRoster = computed(() => match.value?.rosters?.find(x => x.teamId === team.value?.id) ?? null)
  const captain = computed(() => user.value?.userId != null && team.value?.captainId === user.value.userId)
  const onRoster = computed(() => !!user.value?.userId && myRoster.value?.userIds?.includes(user.value.userId) === true)
  const canLock = computed(() => captain.value && match.value?.state === 'Preparing' && !myRoster.value?.locked)
  const allLocked = computed(() => match.value?.rosters?.length === 2 && match.value.rosters.every(x => x.locked))
  const readOnlyMedia = computed(() => staff.value && !onRoster.value)
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
  const judgeOpen = ref(false), judgeAction = ref<JudgeAction>('Pause'), judgeReason = ref(''), forfeitingTeam = ref<string | null>(null)
  const decision = shallowRef<JudgeRequest | null>(null)
  const leaveOpen = ref(false)
  let leaveTo: string | null = null, allowLeave = false
  let disposed = false, request = 0, pending = false, timer: ReturnType<typeof setTimeout> | undefined, clockTimer: ReturnType<typeof setInterval> | undefined
  const elapsedSinceSnapshot = ref(0)
  let observedAt = 0
  const clock = computed(() => formatRoundClock(roundRemainingSeconds(round.value, elapsedSinceSnapshot.value)))

  async function load() {
    if (pending || disposed) return
    pending = true; const id = ++request
    try {
      if (!ctx?.competition.value) await ctx?.refresh()
      const path = { competitionId: competitionId.value, matchId: matchId.value }
      const [result, config, mine, room] = await Promise.all([getLiveSoloMatch({ path, query: { staff: staff.value } }),
        getLiveSoloConfiguration({ path: { competitionId: path.competitionId } }),
        getMyTeamEndpoint({ path: { competitionId: path.competitionId } }), getLiveSoloMedia({ path })])
      if (disposed || id !== request) return
      if (result.error || !result.data || config.error || !config.data) throw parseLiveSoloError(result.error ?? config.error, message('liveSolo.error.load'))
      match.value = result.data; configuration.value = config.data; team.value = mine.data ?? null
      if (room.response?.status === 404) media.value = null
      else if (room.error) throw parseLiveSoloError(room.error, message('liveSolo.error.media'))
      else media.value = room.data ?? null
      if (result.data.currentRoundId) {
        const current = await getLiveSoloRound({ path: { ...path, roundId: result.data.currentRoundId }, query: { staff: staff.value } })
        if (disposed || id !== request) return
        if (current.error || !current.data) throw parseLiveSoloError(current.error, message('liveSolo.error.load'))
        round.value = current.data; observedAt = performance.now(); elapsedSinceSnapshot.value = 0
      }
      else round.value = null
      const names = new Map(memberNames.value)
      if (user.value?.userId) names.set(user.value.userId, user.value.userName ?? '—')
      const missing = (mine.data?.memberIds ?? []).filter(member => !names.has(member))
      const profiles = await Promise.all(missing.map(userId => userProfileGet({ path: { userId } })))
      if (disposed || id !== request) return
      profiles.forEach((profile, index) => { if (profile.data?.userName) names.set(missing[index]!, profile.data.userName) })
      memberNames.value = names
      if (myRoster.value?.locked) selectedRoster.value = [...(myRoster.value.userIds ?? [])]
      else selectedRoster.value = selectedRoster.value.filter(member => mine.data?.memberIds?.includes(member))
      error.value = null
    }
    catch (cause) {
      if (!disposed && id === request) { error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage; await screen.disconnect() }
    }
    finally { pending = false; loading.value = false }
  }
  async function operation(work: () => PromiseLike<{ error?: unknown; response?: Response }>) {
    if (busy.value) return
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
    if (!captain.value || !myRoster.value?.locked || !match.value?.concurrencyStamp) return
    await operation(() => confirmLiveSoloReady({ path: { competitionId: competitionId.value, matchId: matchId.value }, body: { expectedStamp: match.value!.concurrencyStamp! } }))
  }
  async function prepareMedia() {
    if (!allLocked.value || (!judge.value && !onRoster.value) || !match.value?.concurrencyStamp) return
    await operation(() => prepareLiveSoloMedia({ path: { competitionId: competitionId.value, matchId: matchId.value }, body: { expectedMatchStamp: match.value!.concurrencyStamp! } }))
  }
  async function prepareRound() {
    if (!judge.value || !match.value?.concurrencyStamp) return
    await operation(() => prepareLiveSoloRound({ path: { competitionId: competitionId.value, matchId: matchId.value }, body: { expectedStamp: match.value!.concurrencyStamp! } }))
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
  function setJudgeOpen(open: boolean) { judgeOpen.value = open }
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
  async function back() { await router.push(`/competitions/${competitionId.value}/live-solo`) }
  async function tick() { await load(); if (!disposed) timer = setTimeout(tick, 5000) }
  onMounted(() => {
    void tick(); window.addEventListener('beforeunload', beforeUnload)
    clockTimer = setInterval(() => { elapsedSinceSnapshot.value = performance.now() - observedAt }, 250)
  })
  watch(() => user.value?.userId, () => { void screen.disconnect() })
  onScopeDispose(() => {
    disposed = true; request++; if (timer) clearTimeout(timer); if (clockTimer) clearInterval(clockTimer)
    window.removeEventListener('beforeunload', beforeUnload)
  })
  return { competitionId, matchId, match, round, media, configuration, loading, busy, team, myRoster, captain, onRoster,
    canLock, allLocked, rosterOptions, selectedRoster, toggleRoster, lockRoster, ready, prepareMedia, prepareRound, start,
    staff, readOnlyMedia, judge, screens, myScreen, clock, stateKey: computed(() => matchStateKey(match.value?.state)), back, load,
    judgeOpen, judgeAction, judgeReason, forfeitingTeam, openDecision, confirmDecision, setJudgeOpen,
    leaveOpen, setLeaveOpen, confirmLeave, Workspace: markRaw(CompetitionParticipantWorkspace),
    Questions: markRaw(LiveSoloQuestions), canSubmit: computed(() => onRoster.value && canPlayLiveSolo(match.value, round.value)),
    flagDock: computed(() => `live-solo-flag-${matchId.value}`),
    questionsVisible: computed(() => onRoster.value && !!round.value?.startedAt && ['Running', 'ConfirmingResult'].includes(round.value.state ?? '')),
    ...screen, mediaError: screen.error, error }
}
export type LiveSoloMatchState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloMatch>>
