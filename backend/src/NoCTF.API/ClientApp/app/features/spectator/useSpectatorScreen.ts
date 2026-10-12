import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { getAwdSpectatorEndpoint, getKohSpectatorEndpoint } from '../../api'
import type { NoCtfapiEndpointsCompetitionsSpectatorScreenResponse as Screen, NoCtfapiEndpointsCompetitionsSpectatorChangeResponse as Change } from '../../api'
import { watchSpectator } from './spectator-realtime'
import { SpectatorScene } from '../../lib/spectator-scene'
import type { SpectatorSceneState, SpectatorNode } from '../../lib/spectator-scene'
import { SpectatorChangeWindow } from './spectator-sync'
import { controlIntervals } from './spectator-timeline'

export function useSpectatorScreen(mode: 'Awd' | 'Koh') {
  const route = useRoute(), { t } = useLocale()
  const id = String(route.params.id)
  const snapshot = shallowRef<Screen | null>(null), failed = ref(false), loading = ref(true), fallback = ref(false)
  const focus = ref('all'), teamFocus = ref('all'), challengeFocus = ref('all'), group = ref(0), minutes = ref('10')
  const reduced = ref(false), fps = ref<30 | 60>(60), locked = ref(true), carouselLocked = ref(false)
  const hovering = ref(false), focused = ref(false), carouselIndex = ref(0), time = ref(Date.now()), receivedAt = ref(0)
  const transferText = ref('')
  let transferTimer: ReturnType<typeof setTimeout> | undefined
  const summaryCount = ref(0), fullscreenActive = ref(false), fullscreenFailed = ref(false)
  useHead({ title: computed(() => `${t(mode === 'Awd' ? 'spectator.awd' : 'spectator.koh')} · ${snapshot.value?.competitionName ?? 'NoCTF'}`) })
  const diagnostics = import.meta.dev && route.query.diagnostics === '1'
  const metricsText = ref('')
  let scene: SpectatorScene | null = null, disposeHub: (() => void) | undefined
  let timer: ReturnType<typeof setInterval> | undefined, pendingTimer: ReturnType<typeof setTimeout> | undefined
  let disposed = false, inFlight = false, queued = false, epoch = 0, abort: AbortController | null = null
  const changes = new SpectatorChangeWindow<Change>()
  const teams = computed(() => snapshot.value?.teams ?? [])
  const challenges = computed(() => [...(snapshot.value?.challenges ?? [])].sort((a, b) => (a.competitionChallengeId ?? '').localeCompare(b.competitionChallengeId ?? '')))
  const rankTeams = computed(() => [...teams.value].sort((a, b) => (a.rank ?? Infinity) - (b.rank ?? Infinity)))
  const stableTeams = computed(() => [...teams.value].sort((a, b) => (a.id ?? '').localeCompare(b.id ?? '')))
  const nodes = computed<SpectatorNode[]>(() => mode === 'Awd' ? stableTeams.value.map(team => ({
    id: team.id!, name: team.name ?? '', services: challenges.value.map(c =>
      snapshot.value?.services?.find(s => s.teamId === team.id && s.challengeId === c.competitionChallengeId)?.health ?? 'Unknown'),
  })) : challenges.value.map(c => {
    const point = snapshot.value?.points?.find(p => p.challengeId === c.competitionChallengeId)
    const owner = stableTeams.value.findIndex(team => team.id === point?.controllerId)
    return { id: c.competitionChallengeId!, name: c.title ?? '', owner: owner >= 0 ? owner : undefined, controllerName: teamName(point?.controllerId), ownerName: `${controlName(point?.controllerId, point?.health)}${point?.health === 'Unknown' && point.controllerId ? ' · ' + t('spectator.unknown') : ''}`, health: point?.health }
  }))
  const pageSize = computed(() => mode === 'Awd' ? 32 : 12)
  const groups = computed(() => Math.max(1, Math.ceil(nodes.value.length / pageSize.value)))
  const visibleNodes = computed(() => nodes.value.slice(group.value * pageSize.value, (group.value + 1) * pageSize.value))
  const sceneState = computed<SpectatorSceneState>(() => ({ mode, nodes: visibleNodes.value,
    focus: focus.value === 'team' && mode === 'Awd' ? teamFocus.value : focus.value === 'challenge' && mode === 'Koh' ? challengeFocus.value : '',
    highlighted: focus.value === 'team' ? (mode === 'Awd' ? [teamFocus.value] : (snapshot.value?.points ?? []).filter(p => p.controllerId === teamFocus.value).map(p => p.challengeId!)) : focus.value === 'challenge' && mode === 'Koh' ? [challengeFocus.value] : [],
    serviceFocus: mode === 'Awd' && focus.value === 'challenge' ? challenges.value.findIndex(c => c.competitionChallengeId === challengeFocus.value) : -1,
    fps: fps.value, reduced: reduced.value || snapshot.value?.scope !== 'Live', locked: locked.value }))
  const team = computed(() => focus.value === 'team' && teamFocus.value !== 'all'
    ? stableTeams.value.find(t => t.id === teamFocus.value)
    : stableTeams.value[carouselIndex.value % Math.max(1, teams.value.length)])
  const services = computed(() => challenges.value.map(c => ({ ...c,
    ...snapshot.value?.services?.find(s => s.teamId === team.value?.id && s.challengeId === c.competitionChallengeId),
  })))
  const point = computed(() => snapshot.value?.points?.find(p => p.challengeId === challengeFocus.value) ?? snapshot.value?.points?.[0])
  const pointTitle = computed(() => challenges.value.find(c => c.competitionChallengeId === point.value?.challengeId)?.title)
  const serverNow = computed(() => Date.parse(snapshot.value?.serverTime ?? '') + time.value - receivedAt.value)
  const stale = computed(() => failed.value || (receivedAt.value > 0 && time.value - receivedAt.value > 25000))
  const frozen = computed(() => snapshot.value?.scope === 'Frozen')
  const clockText = computed(() => {
    if (!snapshot.value?.round || frozen.value || snapshot.value.status !== 'Running') return '—'
    const seconds = Math.max(0, Math.ceil((Date.parse(snapshot.value.round.endAt!) - serverNow.value) / 1000))
    return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`
  })
  const statusText = computed(() => {
    if (snapshot.value?.scope === 'Hidden') return t('spectator.hidden')
    if (frozen.value) return t('spectator.frozen')
    if (snapshot.value?.status === 'Running' && Date.parse(snapshot.value?.hardeningEndsAt ?? '') > serverNow.value) return t('spectator.hardening')
    return t(snapshot.value?.status === 'Running' ? 'spectator.running' : snapshot.value?.status === 'Paused' ? 'spectator.paused' : 'spectator.standby')
  })
  const history = computed(() => (snapshot.value?.changes ?? []).filter(c =>
    (challengeFocus.value === 'all' || c.challengeId === challengeFocus.value)
    && (teamFocus.value === 'all' || c.teamId === teamFocus.value || c.targetTeamId === teamFocus.value)).slice(0, 30))
  const historyPaused = ref(false), pinnedHistory = shallowRef<Screen | null>(null)
  const historyScreen = computed(() => historyPaused.value ? pinnedHistory.value : snapshot.value)
  const timeline = computed(() => {
    const data = historyScreen.value, end = Date.parse(data?.dataAsOf ?? ''), start = end - Number(minutes.value) * 60000
    const width = end - start
    return challenges.value.filter(c => challengeFocus.value === 'all' || c.competitionChallengeId === challengeFocus.value).map(c => {
      const events = (data?.changes ?? []).filter(e => e.challengeId === c.competitionChallengeId)
      return { id: c.competitionChallengeId, name: c.title,
        segments: controlIntervals(events, start, end).map((segment, index) => ({
          ...segment, id: index, owner: stableTeams.value.findIndex(t => t.id === segment.controllerId) % 4,
          style: { left: `${(segment.start - start) / width * 100}%`, width: `${(segment.end - segment.start) / width * 100}%` },
          label: `${formatTime(new Date(segment.start).toISOString())} · ${controlName(segment.controllerId, segment.unknown ? 'Unknown' : 'Healthy')}`,
        })),
        marks: events.filter(e => e.kind !== 'DetectionFailed').map(e => ({ id: e.id, style: { left: `${(Date.parse(e.at!) - start) / width * 100}%` } })),
      }
    })
  })
  const timelineTicks = computed(() => !historyScreen.value?.dataAsOf ? [] : Array.from({ length: 6 }, (_, i) => formatTime(new Date(Date.parse(historyScreen.value?.dataAsOf ?? '') - Number(minutes.value) * 60000 * (1 - i / 5)).toISOString())))
  function toggleHistory() { historyPaused.value = !historyPaused.value; pinnedHistory.value = historyPaused.value ? snapshot.value : null }
  const fallbackRows = computed(() => visibleNodes.value.map(node => ({ ...node,
    services: mode === 'Awd' ? challenges.value.map((c, i) => ({ name: c.title, health: node.services?.[i] ?? 'Unknown' })) : [],
    controller: mode === 'Koh' ? node.ownerName ?? t('spectator.unknown') : '',
  })))
  const settlements = computed(() => snapshot.value?.settlements?.filter(x => x.teamId === team.value?.id).slice(0, 3) ?? [])
  function teamName(teamId: string | null | undefined) { return teams.value.find(x => x.id === teamId)?.name ?? t('spectator.uncontrolled') }
  function controlName(teamId: string | null | undefined, health: string | undefined) {
    return health === 'Unknown' ? teamId ? t('spectator.lastValid', { team: teamName(teamId) }) : t('spectator.unknown') : teamName(teamId)
  }
  function challengeName(challengeId: string | undefined) { return challenges.value.find(x => x.competitionChallengeId === challengeId)?.title ?? '—' }
  function formatTime(value: string | null | undefined) { return value ? new Date(value).toLocaleTimeString() : t('spectator.noCheck') }
  function healthText(value: string | undefined) { return t(value === 'Healthy' ? 'spectator.healthy' : value === 'Down' ? 'spectator.down' : 'spectator.unknown') }
  function eventText(value: Change) {
    return value.kind === 'Attack' ? `${teamName(value.teamId)} → ${teamName(value.targetTeamId)}`
      : value.kind === 'DetectionFailed' ? t('spectator.detectionFailed')
        : value.kind === 'Uncontrolled' ? t('spectator.uncontrolled') : teamName(value.teamId)
  }
  function controlledCount(teamId: string | undefined) { return snapshot.value?.points?.filter(p => p.controllerId === teamId).length ?? 0 }
  async function refresh() {
    if (disposed || document.hidden) return
    if (inFlight) { queued = true; return }
    inFlight = true; const generation = epoch; abort = new AbortController()
    try {
      const endpoint = mode === 'Awd' ? getAwdSpectatorEndpoint : getKohSpectatorEndpoint
      const response = await endpoint({ path: { competitionId: id }, query: { limit: 2000, minutes: Number(minutes.value) }, signal: abort.signal })
      if (disposed || generation !== epoch) return
      if (response.response?.status === 404 || response.response?.status === 403 || response.response?.status === 401) {
        snapshot.value = null; pinnedHistory.value = null; historyPaused.value = false; changes.reset(); failed.value = true; scene?.update({ ...sceneState.value, nodes: [] }); return
      }
      if (response.response?.status === 202) { snapshot.value = null; pinnedHistory.value = null; historyPaused.value = false; changes.reset(); loading.value = true; schedule(); return }
      if (!response.data || response.error) throw new Error('spectator-read')
      const data = response.data as Screen
      // Read the complete bounded window before committing any part of a version.
      // This also supplies the initial identity baseline, so old pages never replay.
      let cursor = data.nextCursor
      for (let page = 0; cursor && page < 8; page++) {
        const more = await endpoint({ path: { competitionId: id }, query: { limit: 2000, minutes: Number(minutes.value), cursor }, signal: abort.signal })
        if (disposed || generation !== epoch) return
        if (more.response?.status === 401 || more.response?.status === 403 || more.response?.status === 404 || (more.data as Screen | undefined)?.scope === 'Hidden') {
          snapshot.value = null; pinnedHistory.value = null; historyPaused.value = false; changes.reset(); failed.value = true; return
        }
        if (!more.data || more.error || (more.data as Screen).version !== data.version || (more.data as Screen).scope !== data.scope) { schedule(); return }
        const pageData = more.data as Screen
        data.changes = [...(data.changes ?? []), ...(pageData.changes ?? [])]
        cursor = pageData.nextCursor
      }
      if (cursor) { schedule(); return }
      data.nextCursor = null
      if (snapshot.value && BigInt(data.version ?? '0') < BigInt(snapshot.value.version ?? '0') && data.scope === 'Live') return
      const events = changes.accept(data.changes ?? [], data.dataAsOf!, data.scope === 'Live')
      const previous = snapshot.value
      if (mode === 'Koh' && data.scope === 'Live' && previous?.scope === 'Live') {
        const changed = data.points?.find(p => previous.points?.some(old => old.challengeId === p.challengeId && old.controllerId !== p.controllerId))
        if (changed && (changed.health !== 'Unknown' || changed.controllerId)) {
          const prior = previous.points?.find(p => p.challengeId === changed.challengeId)
          transferText.value = t('spectator.transfer', { point: challengeName(changed.challengeId), from: controlName(prior?.controllerId, prior?.health), to: controlName(changed.controllerId, changed.health) })
          clearTimeout(transferTimer); transferTimer = setTimeout(() => { transferText.value = '' }, 4500)
        }
      }
      if (data.scope !== 'Live') { transferText.value = ''; clearTimeout(transferTimer) }
      snapshot.value = data; receivedAt.value = Date.now(); failed.value = false
      if (data.scope !== 'Live') { pinnedHistory.value = null; historyPaused.value = false }
      if (data.scope === 'Hidden') changes.reset()
      const attacks = events.filter(x => x.kind === 'Attack')
      summaryCount.value = Math.max(0, attacks.length - 30)
      // Layout is committed before animation, while score always comes from this snapshot.
      scene?.update(sceneState.value)
      scene?.enqueue(attacks.slice(-30).map(x => ({ id: x.id!, source: x.teamId!, target: x.targetTeamId!, service: challenges.value.findIndex(c => c.competitionChallengeId === x.challengeId) })))
    } catch { if (!disposed && generation === epoch) failed.value = true }
    finally { inFlight = false; loading.value = false; if (queued) { queued = false; schedule() } }
  }
  function schedule() { if (pendingTimer || disposed) return; pendingTimer = setTimeout(() => { pendingTimer = undefined; void refresh() }, 500) }
  function mountScene(element: HTMLElement) {
    try { if (import.meta.dev && route.query.webgl === '0') throw new Error('fallback-preview'); scene = new SpectatorScene(element, () => { fallback.value = true }); scene.update(sceneState.value) }
    catch { scene?.dispose(); scene = null; fallback.value = true }
    return () => { scene?.dispose(); scene = null }
  }
  function previousTeam() { carouselIndex.value = (carouselIndex.value - 1 + teams.value.length) % Math.max(1, teams.value.length) }
  function nextTeam() { carouselIndex.value++ }
  function previousGroup() { group.value = (group.value - 1 + groups.value) % groups.value }
  function nextGroup() { group.value = (group.value + 1) % groups.value }
  function toggleReduced() { reduced.value = !reduced.value }
  function toggleFps() { fps.value = fps.value === 60 ? 30 : 60 }
  function toggleLock() { locked.value = !locked.value }
  function toggleCarousel() { carouselLocked.value = !carouselLocked.value }
  function enterHover() { hovering.value = true }
  function leaveHover() { hovering.value = false }
  function enterFocus() { focused.value = true }
  function leaveFocus(event: FocusEvent) { if (!(event.currentTarget as HTMLElement).contains(event.relatedTarget as Node | null)) focused.value = false }
  const fullscreenChanged = () => { fullscreenActive.value = !!document.fullscreenElement }
  async function fullscreen() {
    fullscreenFailed.value = false
    try { if (document.fullscreenElement) await document.exitFullscreen(); else await document.querySelector<HTMLElement>('[data-spectator-screen]')?.requestFullscreen() }
    catch { fullscreenFailed.value = true }
  }
  function back() { return navigateTo(`/competitions/${id}`) }
  const visibility = () => { if (document.hidden) { epoch++; abort?.abort() } else void refresh() }
  watch(sceneState, state => scene?.update(state))
  watch(minutes, () => { epoch++; changes.reset(); historyPaused.value = false; pinnedHistory.value = null; void refresh() })
  watch([focus, teamFocus, challengeFocus], () => {
    const index = focus.value === 'team' ? nodes.value.findIndex(n => mode === 'Awd' ? n.id === teamFocus.value : snapshot.value?.points?.some(p => p.challengeId === n.id && p.controllerId === teamFocus.value))
      : focus.value === 'challenge' && mode === 'Koh' ? nodes.value.findIndex(n => n.id === challengeFocus.value) : -1
    if (index >= 0) group.value = Math.floor(index / pageSize.value)
  })
  watch(groups, value => { if (group.value >= value) group.value = 0 })
  onMounted(() => {
    reduced.value = matchMedia('(prefers-reduced-motion: reduce)').matches
    void refresh()
    disposeHub = watchSpectator(id, schedule)
    let seconds = 0
    timer = setInterval(() => {
      if (document.hidden) return
      time.value = Date.now(); seconds++
      if (diagnostics && scene) metricsText.value = JSON.stringify({ ...scene.metrics(), seconds, heapMiB: Math.round(((performance as Performance & { memory?: { usedJSHeapSize: number } }).memory?.usedJSHeapSize ?? 0) / 1048576) })
      if (seconds % 10 === 0) void refresh()
      if (seconds % 8 === 0 && !carouselLocked.value && !hovering.value && !focused.value) nextTeam()
    }, 1000)
    document.addEventListener('visibilitychange', visibility)
    document.addEventListener('fullscreenchange', fullscreenChanged)
  })
  onBeforeUnmount(() => { disposed = true; clearTimeout(transferTimer); epoch++; abort?.abort(); disposeHub?.(); clearInterval(timer); clearTimeout(pendingTimer); document.removeEventListener('visibilitychange', visibility); document.removeEventListener('fullscreenchange', fullscreenChanged) })
  return { t, mode, transferText, fullscreenActive, fullscreenFailed, diagnostics, metricsText, snapshot, loading, failed, fallback, focus, teamFocus, challengeFocus, minutes, reduced, fps, locked,
    carouselLocked, group, groups, visibleNodes, fallbackRows, timeline, timelineTicks, historyPaused, toggleHistory, rankTeams, teams, challenges, team, services, point, pointTitle,
    stale, frozen, clockText, statusText, history, settlements, summaryCount,
    teamName, controlName, challengeName, formatTime, healthText, eventText, controlledCount, mountScene, previousTeam, nextTeam,
    previousGroup, nextGroup, toggleReduced, toggleFps, toggleLock, toggleCarousel, enterHover, leaveHover,
    enterFocus, leaveFocus, fullscreen, back, refresh }
}
