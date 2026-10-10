import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { getLiveSoloMatch, listLiveSoloResultCorrections, getLiveSoloResultCorrection, previewLiveSoloResultCorrection,
  beginLiveSoloResultCorrection, resolveLiveSoloResultCorrection } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloResultCorrectionResponse as Correction,
  NoCtfapiEndpointsLiveSoloLiveSoloCorrectionPreviewResponse as Preview } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { canManageLiveSolo } from './settings-draft'
import { allReplaysConfirmed, correctionScoreValid, replayConsents } from './correction-state'
import { matchStateKey } from './live-solo-state'

type Decision = 'Begin' | 'Apply' | 'Cancel'
export function useLiveSoloCorrections() {
  const route = useRoute(), router = useRouter(), context = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), matchId = computed(() => route.params.matchId as string)
  const correctionId = computed(() => typeof route.params.correctionId === 'string' ? route.params.correctionId : null)
  const writable = computed(() => canManageLiveSolo(context?.competition.value?.administrationRole))
  const match = ref<Match | null>(null), correction = ref<Correction | null>(null), preview = ref<Preview | null>(null), history = ref<Correction[]>([])
  const loading = ref(true), busy = ref(false), error = ref<UiMessage | null>(null), decision = ref<Decision | null>(null)
  const winner = ref(''), leftWins = ref(0), rightWins = ref(0), reason = ref(''), resolutionReason = ref(''), selected = ref(new Set<string>())
  const leaveOpen = ref(false)
  const pending = computed(() => correction.value?.state === 'Pending')
  const impacts = computed(() => correction.value?.downstream ?? preview.value?.downstream ?? [])
  const winnerOptions = computed(() => match.value ? [{ value: match.value.leftTeamId, label: match.value.leftTeamName ?? '—' },
    { value: match.value.rightTeamId, label: match.value.rightTeamName ?? '—' }].filter(row => row.value).map(row => ({ ...row, value: row.value! })) : [])
  const validProposal = computed(() => !!match.value && correctionScoreValid(winner.value, match.value.leftTeamId, match.value.rightTeamId,
    match.value.requiredWins ?? 2, leftWins.value, rightWins.value)
    && (winner.value !== match.value.winnerTeamId || leftWins.value !== match.value.leftWins || rightWins.value !== match.value.rightWins)
    && reason.value.trim().length > 0 && reason.value.trim().length <= 4000)
  const canApply = computed(() => pending.value && writable.value && resolutionReason.value.trim().length > 0
    && resolutionReason.value.trim().length <= 4000 && allReplaysConfirmed(impacts.value, selected.value))
  const dirty = computed(() => correction.value == null && !!reason.value.trim() || pending.value && (!!resolutionReason.value.trim() || selected.value.size > 0))
  let disposed = false, request = 0, allowLeave = false, leaveTo: string | null = null
  function path() { return { competitionId: competitionId.value, matchId: matchId.value } }
  function entity(id: string, source = matchId.value) { return `/competitions/${competitionId.value}/live-solo/corrections/${source}/${id}` }
  async function load() {
    if (disposed || busy.value || dirty.value || decision.value) return
    const generation = ++request; loading.value = true
    try {
      if (!context?.competition.value) await context?.refresh()
      const [current, listing] = await Promise.all([getLiveSoloMatch({ path: path(), query: { staff: true } }), listLiveSoloResultCorrections({ path: path() })])
      if (disposed || generation !== request) return
      if (current.error || !current.data || listing.error || !listing.data) throw parseLiveSoloError(current.error ?? listing.error, message('liveSolo.error.load'))
      match.value = current.data; history.value = listing.data
      preview.value = null; selected.value = new Set()
      if (!correctionId.value && current.data.pendingCorrectionId && current.data.pendingCorrectionMatchId) {
        allowLeave = true; await router.replace(entity(current.data.pendingCorrectionId, current.data.pendingCorrectionMatchId)); return
      }
      if (correctionId.value) {
        const detail = await getLiveSoloResultCorrection({ path: { ...path(), correctionId: correctionId.value } })
        if (disposed || generation !== request) return
        if (detail.error || !detail.data) throw parseLiveSoloError(detail.error, message('liveSolo.error.load'))
        correction.value = detail.data
      }
      else { winner.value = current.data.winnerTeamId ?? ''; leftWins.value = current.data.leftWins ?? 0; rightWins.value = current.data.rightWins ?? 0 }
      error.value = null
    }
    catch (cause) { if (!disposed && generation === request) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { if (!disposed && generation === request) loading.value = false }
  }
  function editWinner(value: unknown) {
    if (typeof value !== 'string' || !winnerOptions.value.some(row => row.value === value) || busy.value || decision.value) return
    winner.value = value; preview.value = null
    if (value === match.value?.leftTeamId) { leftWins.value = match.value?.requiredWins ?? 2; rightWins.value = Math.min(rightWins.value, leftWins.value - 1) }
    else { rightWins.value = match.value?.requiredWins ?? 2; leftWins.value = Math.min(leftWins.value, rightWins.value - 1) }
  }
  function tally(side: 'Left' | 'Right', value: number | string) {
    if (busy.value || decision.value) return
    if (side === 'Left') leftWins.value = Number(value); else rightWins.value = Number(value)
    preview.value = null
  }
  function consent(id: string, value: boolean | 'indeterminate') {
    if (busy.value || decision.value) return
    const next = new Set(selected.value); if (value === true) next.add(id); else next.delete(id); selected.value = next
  }
  async function assess() {
    if (!writable.value || !validProposal.value || busy.value || correction.value) return
    busy.value = true
    try {
      const result = await previewLiveSoloResultCorrection({ path: path(), body: { winnerTeamId: winner.value, leftWins: leftWins.value, rightWins: rightWins.value } })
      if (disposed) return
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.correction.changed'))
      preview.value = result.data; error.value = null
    }
    catch (cause) { if (!disposed) error.value = parseLiveSoloError(cause, message('liveSolo.correction.changed')).displayMessage }
    finally { if (!disposed) busy.value = false }
  }
  function open(value: Decision) {
    if (busy.value || decision.value || !writable.value || value === 'Begin' && (!preview.value || !validProposal.value)
      || value === 'Apply' && !canApply.value || value === 'Cancel' && (!pending.value || !resolutionReason.value.trim())) return
    decision.value = value
  }
  function setOpen(value: boolean) { if (!value && !busy.value) decision.value = null }
  async function confirm() {
    const action = decision.value
    if (!action || busy.value || !writable.value || action === 'Begin' && (!preview.value || !validProposal.value)
      || action === 'Apply' && !canApply.value || action === 'Cancel' && (!pending.value || !resolutionReason.value.trim())) return
    busy.value = true
    try {
      const result = action === 'Begin' && preview.value
        ? await beginLiveSoloResultCorrection({ path: path(), body: { winnerTeamId: winner.value, leftWins: leftWins.value, rightWins: rightWins.value, previewId: preview.value.previewId!, reason: reason.value.trim() } })
        : correction.value
          ? await resolveLiveSoloResultCorrection({ path: { ...path(), correctionId: correction.value.id! }, body: { expectedStamp: correction.value.concurrencyStamp!,
            apply: action === 'Apply', reason: resolutionReason.value.trim(), replays: action === 'Apply' ? replayConsents(impacts.value, selected.value) : [] } }) : null
      if (disposed) return
      if (result?.error || !result?.data) throw parseLiveSoloError(result?.error, message('liveSolo.error.operation'))
      decision.value = null; error.value = null; selected.value = new Set(); resolutionReason.value = ''; preview.value = null
      if (action === 'Begin') { reason.value = ''; allowLeave = true; await router.replace(entity(result.data.id!)); return }
      correction.value = result.data
      const [current, listing] = await Promise.all([getLiveSoloMatch({ path: path(), query: { staff: true } }), listLiveSoloResultCorrections({ path: path() })])
      if (!disposed) { if (current.data) match.value = current.data; if (listing.data) history.value = listing.data }
    }
    catch (cause) {
      if (disposed) return
      decision.value = null; preview.value = null; selected.value = new Set()
      error.value = parseLiveSoloError(cause, message('liveSolo.correction.changed')).displayMessage
      try {
        if (correction.value?.id) {
          const latest = await getLiveSoloResultCorrection({ path: { ...path(), correctionId: correction.value.id } })
          if (!disposed && latest.data) correction.value = latest.data
        }
        else {
          const latest = await getLiveSoloMatch({ path: path(), query: { staff: true } })
          if (!disposed && latest.data?.pendingCorrectionId && latest.data.pendingCorrectionMatchId) {
            reason.value = ''; allowLeave = true; await router.replace(entity(latest.data.pendingCorrectionId, latest.data.pendingCorrectionMatchId))
          }
        }
      } catch { /* Keep the original failure and the explicit reload action. */ }
    }
    finally { if (!disposed) busy.value = false }
  }
  async function show(id: string) { await router.push(entity(id)) }
  async function newProposal() { await router.push(`/competitions/${competitionId.value}/live-solo/corrections/${matchId.value}`) }
  async function enterMatch(id: string) { await router.push(`/competitions/${competitionId.value}/live-solo/matches/${id}`) }
  async function back() { await router.push(`/competitions/${competitionId.value}/live-solo/bracket`) }
  function setLeave(value: boolean) { if (!busy.value) leaveOpen.value = value }
  async function leave() { if (busy.value || !leaveTo) return; allowLeave = true; leaveOpen.value = false; await router.push(leaveTo) }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(to => { if (busy.value && !allowLeave) return false; if (!dirty.value || allowLeave) return true; leaveTo = to.fullPath; leaveOpen.value = true; return false })
  onMounted(() => { window.addEventListener('beforeunload', beforeUnload); void load() })
  onScopeDispose(() => { disposed = true; request++; window.removeEventListener('beforeunload', beforeUnload) })
  return { match, correction, preview, history, impacts, pending, winnerOptions, winner, leftWins, rightWins, reason, resolutionReason, selected,
    loading, busy, writable, error, decision, validProposal, canApply, dirty, load, editWinner, tally, consent, assess, open, setOpen, confirm,
    show, newProposal, enterMatch, back, matchStateKey, leaveOpen, setLeave, leave }
}
export type LiveSoloCorrectionsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloCorrections>>
