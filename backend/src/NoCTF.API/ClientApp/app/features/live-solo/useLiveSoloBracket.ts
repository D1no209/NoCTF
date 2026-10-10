import { computed, inject, onMounted, onScopeDispose, ref } from 'vue'
import { adminListTeams, createLiveSoloMatch, generateLiveSoloBracket, getLiveSoloBracket } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloBracketResponse as Bracket, NoCtfapiEndpointsTeamsTeamResponse as Team } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { canManageLiveSolo } from './settings-draft'
import { bracketRows, hasSeededBracket, selectableTeams, reorderSeed } from './bracket-state'

export function useLiveSoloBracket() {
  const route = useRoute(), router = useRouter(), context = inject(competitionContextKey)
  const competitionId = computed(() => route.params.id as string), writable = computed(() => canManageLiveSolo(context?.competition.value?.administrationRole))
  const bracket = ref<Bracket | null>(null), teams = ref<Team[]>([]), loading = ref(true), busy = ref(false), error = ref<UiMessage | null>(null)
  const seedIds = ref<string[]>([]), candidate = ref<string | null>(null), left = ref<string | null>(null), right = ref<string | null>(null), wins = ref<number | null>(null)
  const confirmation = ref<'Generate' | 'Create' | null>(null), plan = ref<{ stamp?: string; teamIds: string[]; wins: number | null } | null>(null)
  const available = computed(() => selectableTeams(teams.value, bracket.value)), seeded = computed(() => hasSeededBracket(bracket.value))
  const options = computed(() => available.value.map(team => ({ value: team.id!, label: team.name ?? '—' })))
  const addOptions = computed(() => options.value.filter(row => !seedIds.value.includes(row.value)))
  const seeds = computed(() => seedIds.value.map((id, index) => ({ id, number: index + 1, name: teams.value.find(team => team.id === id)?.name ?? '—' })))
  const rows = computed(() => bracketRows(bracket.value))
  const matchNumbers = computed(() => new Map(rows.value.map(row => [row.id, row.number])))
  const champion = computed(() => teams.value.find(team => team.id === bracket.value?.championTeamId)?.name ?? null)
  const confirmationNames = computed(() => plan.value?.teamIds.map(id => teams.value.find(team => team.id === id)?.name ?? '—') ?? [])
  let disposed = false, request = 0
  async function load() {
    if (busy.value || disposed) return
    const generation = ++request; loading.value = true
    try {
      if (!context?.competition.value) await context?.refresh()
      const path = { competitionId: competitionId.value }; const graph = await getLiveSoloBracket({ path })
      if (graph.error || !graph.data) throw parseLiveSoloError(graph.error, message('liveSolo.error.load'))
      const roster: Team[] = []; let offset = 0, total = Infinity
      while (offset < total) {
        const result = await adminListTeams({ path, query: { offset, limit: 100, desc: false } })
        if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.load'))
        if (disposed || generation !== request) return
        const batch = result.data.items ?? []; roster.push(...batch); total = result.data.total ?? roster.length
        if (!batch.length) break; offset += batch.length
      }
      if (disposed || generation !== request) return
      bracket.value = graph.data; teams.value = roster; error.value = null
    }
    catch (cause) { if (!disposed && generation === request) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { if (generation === request) loading.value = false }
  }
  function add() { if (!busy.value && candidate.value && addOptions.value.some(row => row.value === candidate.value)) { seedIds.value.push(candidate.value); candidate.value = null } }
  function remove(index: number) { if (!busy.value) seedIds.value.splice(index, 1) }
  function move(index: number, direction: -1 | 1) { if (!busy.value) seedIds.value = reorderSeed(seedIds.value, index, direction) }
  function setWins(value: unknown) { const parsed = Number(value); wins.value = value === '' || value == null ? null : parsed }
  function open(action: 'Generate' | 'Create') {
    if (!writable.value || busy.value || !bracket.value) return
    const ids = action === 'Generate' ? seedIds.value : [left.value, right.value].filter((id): id is string => !!id)
    if (ids.length < 2 || new Set(ids).size !== ids.length || ids.some(id => !available.value.some(team => team.id === id))
      || action === 'Generate' && rows.value.length !== 0 || action === 'Create' && seeded.value
      || wins.value != null && (!Number.isInteger(wins.value) || wins.value < 1 || wins.value > 1024)) { error.value = message('liveSolo.error.configuration'); return }
    plan.value = { stamp: bracket.value.concurrencyStamp, teamIds: [...ids], wins: wins.value }; confirmation.value = action
  }
  function setOpen(value: boolean) { if (!busy.value && !value) { confirmation.value = null; plan.value = null } }
  async function confirm() {
    const target = plan.value, action = confirmation.value; if (!target || !action || busy.value || !writable.value) return
    busy.value = true
    try {
      const path = { competitionId: competitionId.value }
      const result = action === 'Generate' ? await generateLiveSoloBracket({ path, body: { expectedStamp: target.stamp!, teamIds: target.teamIds } })
        : await createLiveSoloMatch({ path, body: { leftTeamId: target.teamIds[0]!, rightTeamId: target.teamIds[1]!, requiredWins: target.wins } })
      if (result.error || !result.data) throw parseLiveSoloError(result.error, message('liveSolo.error.operation'))
      confirmation.value = null; plan.value = null; seedIds.value = []; left.value = null; right.value = null
      if (action === 'Create' && 'id' in result.data && result.data.id) await router.push(`/competitions/${competitionId.value}/live-solo/matches/${result.data.id}`)
    }
    catch (cause) { if (!disposed) { confirmation.value = null; plan.value = null; error.value = parseLiveSoloError(cause, message('liveSolo.error.operation')).displayMessage } }
    finally { busy.value = false; if (!disposed) { const problem = error.value; await load(); if (problem) error.value = problem } }
  }
  async function enter(id: string) { await router.push(`/competitions/${competitionId.value}/live-solo/matches/${id}`) }
  async function back() { await router.push(`/competitions/${competitionId.value}/live-solo`) }
  onMounted(() => { void load() }); onScopeDispose(() => { disposed = true; request++ })
  return { bracket, teams, loading, busy, error, writable, available, options, addOptions, candidate, seeds, seedIds, left, right, wins, rows,
    seeded, champion, matchNumbers, confirmation, confirmationNames, load, add, remove, move, setWins, open, setOpen, confirm, enter, back }
}
export type LiveSoloBracketState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloBracket>>
