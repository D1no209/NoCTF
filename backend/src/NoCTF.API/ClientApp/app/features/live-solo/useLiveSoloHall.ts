import { computed, inject, markRaw, onMounted, onScopeDispose, ref } from 'vue'
import { listLiveSoloMatches, getLiveSoloPlayerPolicy } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloPlayerPolicyResponse as Configuration } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { matchStateKey } from './live-solo-state'
import CompetitionParticipantWorkspace from '~/features/competition/CompetitionParticipantWorkspace.vue'
import { useLiveSoloHub } from './useLiveSoloHub'

export function useLiveSoloHall() {
  const route = useRoute(), router = useRouter()
  const competitionId = computed(() => route.params.id as string)
  const ctx = inject(competitionContextKey)
  const matches = ref<Match[]>([]), configuration = ref<Configuration | null>(null)
  const loading = ref(true), error = ref<UiMessage | null>(null)
  const selected = ref<string | null>(null)
  useLiveSoloHub(competitionId, selected, computed(() => ctx?.competition.value?.administrationRole != null), load)
  const options = computed(() => matches.value.filter(x => x.id).map(row => ({
    value: row.id!, label: `${row.leftTeamName ?? '—'} / ${row.rightTeamName ?? '—'}`, row, stateKey: matchStateKey(row.state),
  })))
  const current = computed(() => matches.value.find(x => x.id === selected.value) ?? null)
  let request = 0, disposed = false, pending = false, refreshQueued = false
  let timer: ReturnType<typeof setTimeout> | undefined
  async function load() {
    if (disposed) return
    if (pending) { refreshQueued = true; return }
    pending = true; const id = ++request
    try {
      if (!ctx?.competition.value) await ctx?.refresh()
      if (ctx?.competition.value?.mode !== 'LiveSolo') { matches.value = []; configuration.value = null; return }
      const [config, rows] = await Promise.all([
        getLiveSoloPlayerPolicy({ path: { competitionId: competitionId.value } }),
        listLiveSoloMatches({ path: { competitionId: competitionId.value }, query: { staff: ctx?.competition.value?.administrationRole != null } }),
      ])
      if (disposed || id !== request) return
      if (config.error || rows.error || !config.data || !rows.data) throw parseLiveSoloError(config.error ?? rows.error, message('liveSolo.error.load'))
      configuration.value = config.data; matches.value = rows.data.items ?? []; error.value = null
      const query = typeof route.query.match === 'string' ? route.query.match : null
      selected.value = matches.value.some(x => x.id === query) ? query
        : matches.value.some(x => x.id === selected.value) ? selected.value : matches.value.find(x => x.id)?.id ?? null
    }
    catch (cause) { if (!disposed && id === request) error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage }
    finally { pending = false; loading.value = false; if (refreshQueued && !disposed) { refreshQueued = false; void load() } }
  }
  async function select(id: string) {
    selected.value = id
    await router.replace({ query: { ...route.query, match: id } })
  }
  async function enter() {
    if (current.value?.id) await router.push(`/competitions/${competitionId.value}/live-solo/matches/${current.value.id}`)
  }
  async function recordings() {
    if (current.value?.id) await router.push({ path: `/competitions/${competitionId.value}/live-solo/recordings/${current.value.id}`,
      query: { ...route.query, staff: ctx?.competition.value?.administrationRole != null ? '1' : undefined } })
  }
  async function settings() { await router.push(`/competitions/${competitionId.value}/live-solo/settings`) }
  async function bracket() { await router.push(`/competitions/${competitionId.value}/live-solo/bracket`) }
  async function groups() { await router.push(`/competitions/${competitionId.value}/live-solo/groups`) }
  async function postgame() { if(current.value?.id)await router.push(`/competitions/${competitionId.value}/live-solo/postgame/${current.value.id}`) }
  async function tick() {
    await load()
    if (!disposed) timer = setTimeout(tick, 5000)
  }
  onMounted(() => { void tick() })
  onScopeDispose(() => { disposed = true; request++; if (timer) clearTimeout(timer) })
  return { competitionId, options, selected, current, configuration, loading, error, select, enter, recordings, settings, bracket, groups, postgame, load,
    finished:computed(()=>ctx?.competition.value?.status==='Finished'),
    staff: computed(() => ctx?.competition.value?.administrationRole != null),
    stateKey: computed(() => matchStateKey(current.value?.state)), Workspace: markRaw(CompetitionParticipantWorkspace) }
}
export type LiveSoloHallState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloHall>>
