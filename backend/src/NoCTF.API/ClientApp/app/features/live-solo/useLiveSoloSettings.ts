import { liveSoloBasePath } from './navigation'
import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { getLiveSoloConfiguration, getLiveSoloBracket, saveLiveSoloConfiguration } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { settingsDraft, validSettings, settingFields, canManageLiveSolo, type LiveSoloSettingsDraft, type NumberSetting, type TournamentStageDraft } from './settings-draft'

export function useLiveSoloSettings() {
  const route = useRoute(), router = useRouter(), context = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), writable = computed(() => canManageLiveSolo(context?.competition.value?.administrationRole))
  const draft = ref<LiveSoloSettingsDraft | null>(null), original = ref(''), loading = ref(true), busy = ref(false), hasMatches = ref(false)
  const error = ref<UiMessage | null>(null), saved = ref(false)
  const leaveOpen = ref(false)
  const dirty = computed(() => draft.value != null && JSON.stringify(draft.value) !== original.value)
  const formatOptions = [{ value: 'SingleElimination', key: 'liveSolo.settings.single' }, { value: 'DoubleElimination', key: 'liveSolo.settings.double' }, { value: 'Swiss', key: 'liveSolo.tournament.swiss' }] as const
  const laneOptions = [{ value: 'Winners', key: 'liveSolo.settings.winners' }, { value: 'Losers', key: 'liveSolo.settings.losers' },
    { value: 'GrandFinal', key: 'liveSolo.settings.grandFinal' }, { value: 'ResetFinal', key: 'liveSolo.settings.resetFinal' }, { value: 'Swiss', key: 'liveSolo.tournament.swiss' }] as const
  const tournamentFields = [
    { key: 'requiredWins', label: 'liveSolo.settings.wins', min: 1, max: 1024 },
    { key: 'advanceCount', label: 'liveSolo.tournament.advanceCount', min: 0, max: 1024 },
  ] as const
  const swissFields = [
    { key: 'swissAdvanceWins', label: 'liveSolo.tournament.advanceWins', min: 1, max: 32 },
    { key: 'swissEliminateLosses', label: 'liveSolo.tournament.eliminateLosses', min: 1, max: 32 },
    { key: 'swissQualificationRequiredWins', label: 'liveSolo.tournament.qualificationBo', min: 1, max: 1024 },
    { key: 'swissEliminationRequiredWins', label: 'liveSolo.tournament.eliminationBo', min: 1, max: 1024 },
  ] as const
  const sections = computed(() => [{ key: 'match', title: 'liveSolo.settings.match', fields: settingFields.filter(x => x.section === 'match') },
    { key: 'timing', title: 'liveSolo.settings.timing', fields: settingFields.filter(x => x.section === 'timing') },
    { key: 'media', title: 'liveSolo.settings.media', fields: draft.value?.platformStreamingEnabled ? settingFields.filter(x => x.section === 'media') : [] }] as const)
  let disposed = false, request = 0, allowLeave = false, leaveTo: string | null = null
  async function load() {
    if (busy.value || disposed) return
    const generation = ++request; loading.value = true
    try {
      if (!context?.competition.value) await context?.refresh()
      const path = { competitionId: competitionId.value }
      const [config, bracket] = await Promise.all([getLiveSoloConfiguration({ path }), getLiveSoloBracket({ path })])
      if (disposed || generation !== request) return
      if (config.error || !config.data || bracket.error || !bracket.data) throw parseLiveSoloError(config.error ?? bracket.error, message('liveSolo.error.load'))
      draft.value = settingsDraft(config.data); original.value = JSON.stringify(draft.value); hasMatches.value = (bracket.data.matches?.length ?? 0) > 0
      error.value = null; saved.value = false
    }
    catch (cause) { if (!disposed && generation === request) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { if (generation === request) loading.value = false }
  }
  function number(key: NumberSetting, value: number | string) { if (draft.value && writable.value) { draft.value[key] = Number(value); saved.value = false } }
  function format(value: unknown) { if (draft.value && writable.value && !hasMatches.value && formatOptions.some(x => x.value === value)) draft.value.bracketFormat = value as LiveSoloSettingsDraft['bracketFormat'] }
  function formatKey(value: TournamentStageDraft['format']) { return formatOptions.find(x => x.value === value)?.key ?? 'liveSolo.settings.single' }
  function addTournamentStage() {
    if (!draft.value || !writable.value || hasMatches.value) return
    draft.value.tournamentStages.push({ name: '', format: draft.value.tournamentStages.length ? 'DoubleElimination' : draft.value.bracketFormat,
      requiredWins: draft.value.requiredWins, advanceCount: draft.value.bracketFormat === 'Swiss' && !draft.value.tournamentStages.length ? 0 : 1,
      swissAdvanceWins: 3, swissEliminateLosses: 3, swissQualificationRequiredWins: 2, swissEliminationRequiredWins: 2 })
  }
  function moveTournamentStage(index: number, direction: -1 | 1) {
    const stages = draft.value?.tournamentStages, next = index + direction
    if (!stages || !writable.value || hasMatches.value || next < 0 || next >= stages.length) return
    ;[stages[index], stages[next]] = [stages[next]!, stages[index]!]
    for (const rule of draft.value!.stageRules) {
      if ((rule.tournamentStage ?? 1) === index + 1) rule.tournamentStage = next + 1
      else if ((rule.tournamentStage ?? 1) === next + 1) rule.tournamentStage = index + 1
    }
  }
  function removeTournamentStage(index: number) {
    if (!draft.value || !writable.value || hasMatches.value) return
    draft.value.tournamentStages.splice(index, 1)
    draft.value.stageRules = draft.value.stageRules.filter(x => (x.tournamentStage ?? 1) !== index + 1)
    for (const row of draft.value.stageRules) if ((row.tournamentStage ?? 1) > index + 1) row.tournamentStage = (row.tournamentStage ?? 1) - 1
  }
  function tournamentNumber(index: number, key: Exclude<keyof TournamentStageDraft, 'name' | 'format'>, value: number | string) {
    const stage = draft.value?.tournamentStages[index]
    if (stage && writable.value && !hasMatches.value) stage[key] = Number(value)
  }
  function tournamentFormat(index: number, value: unknown) {
    const stage = draft.value?.tournamentStages[index]
    if (stage && writable.value && !hasMatches.value && formatOptions.some(x => x.value === value)) {
      stage.format = value as TournamentStageDraft['format']; if (stage.format !== 'Swiss' && stage.advanceCount === 0) stage.advanceCount = 1
    }
  }
  function addStage() { if (draft.value && writable.value) draft.value.stageRules.push({ lane: 'Winners', stage: 1, requiredWins: draft.value.requiredWins }) }
  function removeStage(index: number) { if (draft.value && writable.value) draft.value.stageRules.splice(index, 1) }
  function stageNumber(index: number, key: 'stage' | 'requiredWins' | 'tournamentStage', value: string | number) { const row = draft.value?.stageRules[index]; if (row && writable.value) row[key] = Number(value) }
  function stageLane(index: number, value: unknown) { const row = draft.value?.stageRules[index]; if (row && writable.value && laneOptions.some(x => x.value === value)) row.lane = value as typeof row.lane }
  function enabled(value: boolean | 'indeterminate') { if (draft.value && writable.value) draft.value.enabled = value === true }
  function evidence(value: boolean | 'indeterminate') { if (draft.value && writable.value) { draft.value.staticFlagEvidenceRequired = value === true; saved.value = false } }
  function streaming(value: boolean | 'indeterminate') {
    if (draft.value && writable.value) { draft.value.platformStreamingEnabled = value === true; saved.value = false }
  }
  function recording(value: boolean | 'indeterminate') { if (draft.value && writable.value) draft.value.recordingEnabled = value === true }
  function opponents(value: boolean | 'indeterminate') { if (draft.value && writable.value) draft.value.participantsMayViewOpponents = value === true }
  async function save() {
    if (!draft.value || !writable.value || busy.value || !dirty.value) return
    if (!validSettings(draft.value)) { error.value = message('liveSolo.error.configuration'); return }
    busy.value = true; const generation = ++request
    try {
      const result = await saveLiveSoloConfiguration({ path: { competitionId: competitionId.value }, body: { configuration: draft.value } })
      if (disposed || generation !== request) return
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      draft.value = settingsDraft(result.data); original.value = JSON.stringify(draft.value); error.value = null; saved.value = true
      await context?.refresh()
    }
    catch (cause) { if (!disposed && generation === request) error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage }
    finally { busy.value = false }
  }
  async function back() { await router.push(`${liveSoloBasePath(competitionId.value, route.path)}`) }
  function setLeave(value: boolean) { if (!busy.value) leaveOpen.value = value }
  async function leave() { if (busy.value || !leaveTo) return; allowLeave = true; leaveOpen.value = false; await router.push(leaveTo) }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(to => { if (busy.value) return false; if (!dirty.value || allowLeave) return true; leaveTo = to.fullPath; leaveOpen.value = true; return false })
  onMounted(() => { void load(); window.addEventListener('beforeunload', beforeUnload) })
  onScopeDispose(() => { disposed = true; request++; window.removeEventListener('beforeunload', beforeUnload) })
  return { draft, loading, busy, dirty, writable, hasMatches, error, saved, formatOptions, laneOptions, sections, load, number, format,
    addStage, removeStage, stageNumber, stageLane, enabled, evidence, streaming, recording, opponents, save, back, leaveOpen, setLeave, leave,
    tournamentFields, swissFields, addTournamentStage, moveTournamentStage, removeTournamentStage, tournamentNumber, tournamentFormat, formatKey }
}
export type LiveSoloSettingsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloSettings>>
