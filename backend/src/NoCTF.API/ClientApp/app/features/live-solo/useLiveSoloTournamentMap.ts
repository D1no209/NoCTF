import { computed, nextTick, ref, watch } from 'vue'
import { useFullscreen } from '@vueuse/core'
import { Maximize, Minimize, ZoomIn, ZoomOut, LocateFixed, Trophy } from '@lucide/vue'
import type { NoCtfapiEndpointsLiveSoloLiveSoloBracketResponse as Bracket } from '~/api'
import { tournamentDiagram, tournamentFormatKeys } from './tournament-diagram'

export function useLiveSoloTournamentMap(props: { bracket: Bracket; connected?: boolean; syncing?: boolean }, emit: (event: 'enter', id: string) => void) {
  const selectedStage = ref('1'), focusTeam = ref<string | null>(null), zoom = ref(1), root = ref<HTMLElement | null>(null), showStandings = ref(false)
  const { isFullscreen, toggle } = useFullscreen(root)
  const stageOptions = computed(() => (props.bracket.tournamentStages ?? []).map(stage => ({ value: String(stage.number ?? 1),
    name: stage.rules?.name, number: stage.number ?? 1, formatKey: tournamentFormatKeys[stage.rules?.format ?? 'SingleElimination'], complete: stage.complete })))
  const stage = computed(() => props.bracket.tournamentStages?.find(row => row.number === Number(selectedStage.value)))
  const diagram = computed(() => tournamentDiagram(props.bracket, Number(selectedStage.value)))
  const names = computed(() => new Map((props.bracket.matches ?? []).flatMap(row => [
    [row.match?.leftTeamId, row.match?.leftTeamName], [row.match?.rightTeamId, row.match?.rightTeamName],
  ]).filter((row): row is [string, string] => !!row[0] && !!row[1])))
  const avatars = computed(() => new Map((props.bracket.matches ?? []).flatMap(row => [
    [row.match?.leftTeamId, row.match?.leftAvatarUrl], [row.match?.rightTeamId, row.match?.rightAvatarUrl],
  ]).filter((row): row is [string, string] => !!row[0] && !!row[1])))
  const teamOptions = computed(() => (stage.value?.standings ?? []).filter(row => row.teamId)
    .map(row => ({ value: row.teamId!, label: names.value.get(row.teamId!) ?? '—' })))
  const standings = computed(() => (stage.value?.standings ?? []).map(row => ({ ...row, name: names.value.get(row.teamId ?? '') ?? '—',
    avatarUrl: avatars.value.get(row.teamId ?? ''), initials: Array.from(names.value.get(row.teamId ?? '') ?? '').slice(0, 2).join(''),
    stateKey: stage.value?.complete ? stage.value.qualifiedTeamIds?.includes(row.teamId ?? '') ? 'liveSolo.tournament.qualified' : 'liveSolo.tournament.eliminated'
      : row.state === 'Qualified' ? 'liveSolo.tournament.qualified' : row.state === 'Eliminated' ? 'liveSolo.tournament.eliminated' : 'liveSolo.tournament.active' } as const)))
  const highlighted = computed(() => new Set(diagram.value.nodes.filter(node => !focusTeam.value || node.sides.some(side => side.teamId === focusTeam.value)).map(node => node.id)))
  const groups = computed(() => [
    { key: 'liveSolo.tournament.qualified', rows: standings.value.filter(row => row.stateKey === 'liveSolo.tournament.qualified') },
    { key: 'liveSolo.tournament.eliminated', rows: standings.value.filter(row => row.stateKey === 'liveSolo.tournament.eliminated') },
    { key: 'liveSolo.tournament.active', rows: standings.value.filter(row => row.stateKey === 'liveSolo.tournament.active') },
  ] as const)
  const canvasWidth = computed(() => diagram.value.width + (standings.value.length ? 272 : 0))
  const canvasHeight = computed(() => Math.max(diagram.value.height, standings.value.length * 40 + 192))
  const syncKey = computed(() => props.syncing ? 'liveSolo.tournament.syncing' : props.connected ? 'liveSolo.tournament.live' : 'liveSolo.tournament.polling')
  function setRoot(value: unknown) { if (value instanceof HTMLElement && root.value !== value) { root.value = value; void nextTick(resetZoom) } }
  function zoomIn() { zoom.value = Math.min(1.5, Math.round((zoom.value + 0.1) * 10) / 10) }
  function zoomOut() { zoom.value = Math.max(0.35, Math.round((zoom.value - 0.1) * 100) / 100) }
  function resetZoom() { zoom.value = 1 }
  function locate() {
    const node = diagram.value.nodes.find(n => ['Running', 'Countdown', 'Paused'].includes(n.row.match?.state ?? '')) ?? diagram.value.nodes.find(n => n.row.match?.state === 'Preparing')
    if (node) root.value?.querySelector(`[data-match-id="${node.id}"]`)?.scrollIntoView({ block: 'nearest', inline: 'center', behavior: 'smooth' })
  }
  function enter(id: string) { emit('enter', id) }
  function clearFocus() { focusTeam.value = null }
  watch(() => props.bracket.competitionId, () => { selectedStage.value = '1'; focusTeam.value = null })
  watch([selectedStage, isFullscreen], () => { focusTeam.value = null; void nextTick(resetZoom) })
  return { selectedStage, focusTeam, zoom, root, isFullscreen, toggle, setRoot, stageOptions, stage, diagram, standings, highlighted,
    teamOptions, zoomIn, zoomOut, resetZoom, locate, enter, clearFocus, Maximize, Minimize, ZoomIn, ZoomOut, LocateFixed, Trophy, groups, showStandings,
    canvasWidth, canvasHeight, syncKey }
}
export type LiveSoloTournamentMapState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloTournamentMap>>
