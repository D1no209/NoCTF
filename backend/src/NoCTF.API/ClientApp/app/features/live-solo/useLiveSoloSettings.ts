import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { getLiveSoloConfiguration, getLiveSoloBracket, saveLiveSoloConfiguration } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { settingsDraft, validSettings, settingFields, canManageLiveSolo, type LiveSoloSettingsDraft, type NumberSetting } from './settings-draft'

export function useLiveSoloSettings() {
  const route = useRoute(), router = useRouter(), context = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), writable = computed(() => canManageLiveSolo(context?.competition.value?.administrationRole))
  const draft = ref<LiveSoloSettingsDraft | null>(null), original = ref(''), loading = ref(true), busy = ref(false), hasMatches = ref(false)
  const error = ref<UiMessage | null>(null), saved = ref(false)
  const leaveOpen = ref(false)
  const dirty = computed(() => draft.value != null && JSON.stringify(draft.value) !== original.value)
  const formatOptions = [{ value: 'SingleElimination', key: 'liveSolo.settings.single' }, { value: 'DoubleElimination', key: 'liveSolo.settings.double' }] as const
  const laneOptions = [{ value: 'Winners', key: 'liveSolo.settings.winners' }, { value: 'Losers', key: 'liveSolo.settings.losers' },
    { value: 'GrandFinal', key: 'liveSolo.settings.grandFinal' }, { value: 'ResetFinal', key: 'liveSolo.settings.resetFinal' }] as const
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
  function format(value: unknown) { if (draft.value && writable.value && !hasMatches.value && (value === 'SingleElimination' || value === 'DoubleElimination')) draft.value.bracketFormat = value }
  function addStage() { if (draft.value && writable.value) draft.value.stageRules.push({ lane: 'Winners', stage: 1, requiredWins: draft.value.requiredWins }) }
  function removeStage(index: number) { if (draft.value && writable.value) draft.value.stageRules.splice(index, 1) }
  function stageNumber(index: number, key: 'stage' | 'requiredWins', value: string | number) { const row = draft.value?.stageRules[index]; if (row && writable.value) row[key] = Number(value) }
  function stageLane(index: number, value: unknown) { const row = draft.value?.stageRules[index]; if (row && writable.value && laneOptions.some(x => x.value === value)) row.lane = value as typeof row.lane }
  function enabled(value: boolean | 'indeterminate') { if (draft.value && writable.value) draft.value.enabled = value === true }
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
  async function back() { await router.push(`/competitions/${competitionId.value}/live-solo`) }
  function setLeave(value: boolean) { if (!busy.value) leaveOpen.value = value }
  async function leave() { if (busy.value || !leaveTo) return; allowLeave = true; leaveOpen.value = false; await router.push(leaveTo) }
  function beforeUnload(event: BeforeUnloadEvent) { if (dirty.value) { event.preventDefault(); event.returnValue = '' } }
  onBeforeRouteLeave(to => { if (busy.value) return false; if (!dirty.value || allowLeave) return true; leaveTo = to.fullPath; leaveOpen.value = true; return false })
  onMounted(() => { void load(); window.addEventListener('beforeunload', beforeUnload) })
  onScopeDispose(() => { disposed = true; request++; window.removeEventListener('beforeunload', beforeUnload) })
  return { draft, loading, busy, dirty, writable, hasMatches, error, saved, formatOptions, laneOptions, sections, load, number, format,
    addStage, removeStage, stageNumber, stageLane, enabled, streaming, recording, opponents, save, back, leaveOpen, setLeave, leave }
}
export type LiveSoloSettingsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloSettings>>
