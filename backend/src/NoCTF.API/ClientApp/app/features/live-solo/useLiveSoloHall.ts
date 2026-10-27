import { liveSoloBasePath, liveSoloManagementPath, isLiveSoloManagementPath } from './navigation'
import { computed, inject, markRaw, onMounted, onScopeDispose, ref, watch } from 'vue'
import { deleteLiveSoloMatch, listLiveSoloMatches, getLiveSoloPlayerPolicy, getLiveSoloWorkspace } from '~/api'
import type { NoCtfapiEndpointsLiveSoloLiveSoloMatchResponse as Match, NoCtfapiEndpointsLiveSoloLiveSoloPlayerPolicyResponse as Configuration } from '~/api'
import { competitionContextKey } from '~/utils/labels'
import { message, type UiMessage } from '~/utils/i18n'
import { parseLiveSoloError } from './live-solo-errors'
import { matchStateKey } from './live-solo-state'
import CompetitionParticipantWorkspace from '~/features/competition/CompetitionParticipantWorkspace.vue'
import { useLiveSoloHub } from './useLiveSoloHub'
import { endReasonKeys } from './result-presentation'
import { canManageLiveSolo } from './settings-draft'

export function useLiveSoloHall() {
  const route = useRoute(), router = useRouter()
  const competitionId = computed(() => route.params.id as string)
  const ctx = inject(competitionContextKey)
  const management = computed(() => isLiveSoloManagementPath(route.path))
  const reviewMode = computed(() => route.path.endsWith('/writeups'))
  const hasStaffAccess = computed(() => ctx?.competition.value?.administrationRole != null)
  const staff = computed(() => management.value && hasStaffAccess.value)
  async function manage() { await router.push({ path: liveSoloManagementPath(competitionId.value), query: { match: selected.value ?? undefined } }) }
  const matches = ref<Match[]>([]), configuration = ref<Configuration | null>(null)
  const loading = ref(true), error = ref<UiMessage | null>(null)
  const selected = ref<string | null>(null)
  const activity = ref<import('~/api').NoCtfapiEndpointsLiveSoloLiveSoloWorkspaceResponse | null>(null)
  const { user } = useAuth()
  let activityRequest = 0
  async function loadActivity() {
    const id = ++activityRequest, match = selected.value, actor = user.value?.userId
    if (!match || !actor) return
    const response = await getLiveSoloWorkspace({ path: { competitionId: competitionId.value, matchId: match }, query: { staff: staff.value } })
    if (!disposed && id === activityRequest && actor === user.value?.userId && selected.value === match) activity.value = response.data ?? null
  }
  const activityRows = computed(() => (activity.value?.history ?? []).map(x => ({ id: x.id,
    label: message('liveSolo.activity.opened', { number: x.roundNumber ?? 1, position: (x.position ?? 0) + 1 }), time: x.openedAt ?? null }))
    .concat(activity.value?.match?.resultUpdatedAt ? [{ id: activity.value.match.id,
      label: message('liveSolo.activity.ended'), time: activity.value.match.resultUpdatedAt }] : [])
    .sort((a, b) => (b.time ?? '').localeCompare(a.time ?? '')))
  useLiveSoloHub(competitionId, selected, staff, load)
  const options = computed(() => matches.value.filter(x => x.id).map(row => ({
    value: row.id!, label: `${row.leftTeamName ?? '—'} / ${row.rightTeamName ?? '—'}`, row, stateKey: matchStateKey(row.state),
  })))
  const current = computed(() => matches.value.find(x => x.id === selected.value) ?? null)
  const canManage = computed(() => management.value && canManageLiveSolo(ctx?.competition.value?.administrationRole))
  const deleteTarget = ref<Match | null>(null), deleting = ref(false), deleteError = ref<UiMessage | null>(null)
  const deleteOpen = computed(() => deleteTarget.value != null)
  const deleteLabel = computed(() => `${deleteTarget.value?.leftTeamName ?? '—'} / ${deleteTarget.value?.rightTeamName ?? '—'}`)
  function askDelete() { if (canManage.value && current.value) { deleteTarget.value = current.value; deleteError.value = null } }
  function setDeleteOpen(open: boolean) { if (!open && !deleting.value) { deleteTarget.value = null; deleteError.value = null } }
  async function removeMatch() {
    const matchId = deleteTarget.value?.id, competition = competitionId.value, actor = user.value?.userId
    if (!matchId || !canManage.value || deleting.value) return
    deleting.value = true; deleteError.value = null
    try {
      const response = await deleteLiveSoloMatch({ path: { competitionId: competition, matchId } })
      if (disposed || competition !== competitionId.value || actor !== user.value?.userId) return
      if (response.error || !response.data) throw parseLiveSoloError(response.error, message('liveSolo.matchDeletion.failed'))
      request++; activityRequest++
      deleteTarget.value = null
      const ids = [matchId, ...(response.data.matchIds ?? [])]
      matches.value = matches.value.filter(x => !ids.includes(x.id!))
      if (selected.value === matchId || ids.includes(selected.value!)) {
        selected.value = null; activity.value = null
        await router.replace({ query: { ...route.query, match: undefined } })
      }
      await load()
    } catch (cause) {
      if (!disposed && competition === competitionId.value && actor === user.value?.userId)
        deleteError.value = parseLiveSoloError(cause, message('liveSolo.matchDeletion.failed')).displayMessage
    } finally { deleting.value = false }
  }
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
        listLiveSoloMatches({ path: { competitionId: competitionId.value }, query: { staff: staff.value } }),
      ])
      if (disposed || id !== request) return
      if (config.error || rows.error || !config.data || !rows.data) throw parseLiveSoloError(config.error ?? rows.error, message('liveSolo.error.load'))
      configuration.value = config.data; matches.value = rows.data.items ?? []; error.value = null
      const query = typeof route.query.match === 'string' ? route.query.match : null
      const previousSelection = selected.value
      selected.value = matches.value.some(x => x.id === query) ? query
        : matches.value.some(x => x.id === selected.value) ? selected.value : matches.value.find(x => x.id)?.id ?? null
      if (previousSelection === selected.value) void loadActivity()
    }
    catch (cause) { if (!disposed && id === request) { matches.value = []; activity.value = null; error.value = parseLiveSoloError(cause, message('liveSolo.error.load')).displayMessage } }
    finally { pending = false; loading.value = false; if (refreshQueued && !disposed) { refreshQueued = false; void load() } }
  }
  async function select(id: string) {
    selected.value = id
    await router.replace({ query: { ...route.query, match: id } })
  }
  async function enter() {
    if (reviewMode.value) { if (ctx?.competition.value?.status === 'Finished') await postgame(); return }
    if (current.value?.id) await router.push(`${liveSoloBasePath(competitionId.value, route.path)}/matches/${current.value.id}`)
  }
  async function recordings() {
    if (current.value?.id) await router.push({ path: `${liveSoloBasePath(competitionId.value, route.path)}/recordings/${current.value.id}`,
      query: { ...route.query } })
  }
  async function settings() { await router.push(`${liveSoloManagementPath(competitionId.value)}/settings`) }
  async function bracket() { await router.push(`${liveSoloManagementPath(competitionId.value)}/bracket`) }
  async function pool() { await router.push(`${liveSoloManagementPath(competitionId.value)}/question-pool`) }
  async function groups() { await router.push(`${liveSoloManagementPath(competitionId.value)}/groups`) }
  async function postgame() { if(current.value?.id)await router.push({ path: `${liveSoloBasePath(competitionId.value, route.path)}/postgame/${current.value.id}`, query: reviewMode.value ? { from: 'writeups' } : {} }) }
  async function tick() {
    await load()
    if (!disposed) timer = setTimeout(tick, 5000)
  }
  onMounted(() => { void tick() })
  watch(selected, () => { activity.value = null; void loadActivity() })
  watch([() => user.value?.userId, competitionId], () => {
    deleteTarget.value = null; deleteError.value = null
    request++; activityRequest++; matches.value = []; activity.value = null; configuration.value = null; selected.value = null; void load()
  })
  onScopeDispose(() => { disposed = true; request++; activityRequest++; if (timer) clearTimeout(timer) })
  return { competitionId, options, selected, current, configuration, loading, error, select, enter, recordings, settings, bracket, groups, pool, postgame, load,
    management, reviewMode, hasStaffAccess, manage, activityRows, canManage,
    deleteOpen, deleteLabel, deleting, deleteError, askDelete, setDeleteOpen, removeMatch,
    endReasonKey: computed(() => endReasonKeys[current.value?.endReason ?? 'Unrecorded']),
    finished:computed(()=>ctx?.competition.value?.status==='Finished'),
    staff,
    stateKey: computed(() => matchStateKey(current.value?.state)), Workspace: markRaw(CompetitionParticipantWorkspace) }
}
export type LiveSoloHallState = import('vue').ShallowUnwrapRef<ReturnType<typeof useLiveSoloHall>>
