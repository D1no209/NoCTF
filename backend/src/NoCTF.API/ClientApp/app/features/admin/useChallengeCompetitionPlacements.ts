import { computed, onBeforeUnmount, onMounted, ref, toRefs, watch } from 'vue'
import { ArrowUpRight, ChevronLeft, ChevronRight, Plus, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminCreateCompetitionChallenge, adminListCompetitionChallenges, adminListCompetitions, competitionPosterGet } from '../../api'
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '../../api'
import type { ContentSwapPreset } from '../../motion/useContentSwap'
import { translate } from '../../utils/i18n'
import { parseApiError } from '../../utils/api-error'
import { competitionChallengeConflictMessage } from '../../lib/competition-challenge-conflict'
import { availablePlacementCompetitions, placementManagementPath, projectChallengePlacements, writablePlacementCompetitions } from './challenge-competition-placements'
import type { ChallengeCompetitionPlacement } from './challenge-competition-placements'

export function useChallengeCompetitionPlacements(props: Readonly<{ challengeId: string; mode: NoCtfapiEndpointsCompetitionsGameModeProtocol; disabled: boolean }>) {
  const { user } = useAuth()
  const items = ref<ChallengeCompetitionPlacement[]>([])
  const loading = ref(true)
  const error = ref<string | null>(null)
  const adding = ref(false)
  const targetId = ref('')
  const protectedPosters = ref<Record<string, string>>({})
  const previewMotion = ref<ContentSwapPreset>('film-left')
  const linked = computed(() => items.value.filter(item => item.instances.length > 0))
  const candidates = computed(() => availablePlacementCompetitions(items.value, props.mode))
  const linkedOptions = computed(() => linked.value.map(item => ({
    value: item.competition.id, label: item.competition.title ?? '', posterUrl: posterFor(item.competition),
    href: placementManagementPath(item),
  })))
  const preview = computed(() => candidates.value.find(item => item.competition.id === targetId.value) ?? null)
  const targetIndex = computed(() => candidates.value.findIndex(item => item.competition.id === targetId.value))
  const previewPosterUrl = computed(() => preview.value ? posterFor(preview.value.competition) : null)
  const canAdd = computed(() => !props.disabled && !loading.value && !adding.value && !error.value
    && candidates.value.some(item => item.competition.id === targetId.value))
  let reads: AbortController | undefined
  let generation = 0
  let disposed = false

  function posterFor(competition: ChallengeCompetitionPlacement['competition']) {
    return competition.accessMode === 'StaffOnly' ? protectedPosters.value[competition.id] ?? null : competition.posterUrl
  }
  function clearProtectedPosters() {
    Object.values(protectedPosters.value).forEach(url => URL.revokeObjectURL(url))
    protectedPosters.value = {}
  }

  function selectTarget(value: unknown) {
    const id = typeof value === 'string' ? value : ''
    const nextIndex = candidates.value.findIndex(item => item.competition.id === id)
    if (nextIndex < 0 || adding.value) return
    previewMotion.value = nextIndex < targetIndex.value ? 'film-right' : 'film-left'
    targetId.value = id
  }
  function previousTarget() { if (targetIndex.value > 0) selectTarget(candidates.value[targetIndex.value - 1]!.competition.id) }
  function nextTarget() { if (targetIndex.value < candidates.value.length - 1) selectTarget(candidates.value[targetIndex.value + 1]!.competition.id) }
  async function load() {
    reads?.abort()
    reads = new AbortController()
    const signal = reads.signal
    const request = ++generation
    loading.value = true
    error.value = null
    try {
      const { data, error: failure } = await adminListCompetitions({ query: { includeDeleted: false }, signal })
      if (signal.aborted) return
      if (failure || !data) throw failure
      const competitions = writablePlacementCompetitions(data.items ?? []).filter(item => item.mode === props.mode)
      const results: ChallengeCompetitionPlacement[] = []
      const failures: string[] = []
      const posterBlobs = new Map<string, Blob>()
      let cursor = 0
      await Promise.all(Array.from({ length: Math.min(4, competitions.length) }, async () => {
        while (cursor < competitions.length && !signal.aborted) {
          const competition = competitions[cursor++]!
          const { data, error: failure } = await adminListCompetitionChallenges({
            path: { competitionId: competition.id }, query: { includeDeleted: false }, signal,
          })
          if (signal.aborted) return
          if (failure || !data) failures.push(competition.title ?? '')
          else {
            results.push(projectChallengePlacements(competition, data.items ?? [], props.challengeId))
            if (competition.accessMode === 'StaffOnly' && competition.posterUrl) {
              const { data: image } = await competitionPosterGet({ path: { competitionId: competition.id }, parseAs: 'blob', cache: 'no-store', signal })
              if (image instanceof Blob && image.type.startsWith('image/') && image.size > 0) posterBlobs.set(competition.id, image)
            }
          }
        }
      }))
      if (signal.aborted || request !== generation) return
      clearProtectedPosters()
      protectedPosters.value = Object.fromEntries([...posterBlobs].map(([id, blob]) => [id, URL.createObjectURL(blob)]))
      items.value = results.sort((a, b) => (a.competition.title ?? '').localeCompare(b.competition.title ?? ''))
      if (failures.length) error.value = translate('placements.partialFailure', { competitions: failures.join('、') })
      if (!candidates.value.some(item => item.competition.id === targetId.value)) targetId.value = candidates.value[0]?.competition.id ?? ''
    } catch (failure) {
      if (!signal.aborted && request === generation) error.value = parseApiError(failure, translate('placements.loadFailed')).message
    } finally {
      if (!signal.aborted && request === generation) loading.value = false
    }
  }
  async function addToCompetition() {
    if (!canAdd.value) return
    const target = candidates.value.find(item => item.competition.id === targetId.value)
    if (!target) return
    const actorId = user.value?.userId
    adding.value = true
    error.value = null
    try {
      const { error: failure } = await adminCreateCompetitionChallenge({
        path: { competitionId: target.competition.id },
        body: { challengeId: props.challengeId, customTitle: null, order: target.nextOrder },
      })
      if (disposed || actorId !== user.value?.userId) return
      if (failure) {
        error.value = competitionChallengeConflictMessage(failure) ?? parseApiError(failure).message
        return
      }
      targetId.value = ''
      toast.success(translate('placements.added', { competition: target.competition.title ?? '' }))
      await load()
    } catch (failure) {
      if (!disposed && actorId === user.value?.userId) error.value = parseApiError(failure, translate('placements.addFailed')).message
    } finally { adding.value = false }
  }
  watch(() => [user.value?.userId, props.challengeId, props.mode], () => {
    items.value = []
    clearProtectedPosters()
    targetId.value = ''
    void load()
  })
  onMounted(load)
  onBeforeUnmount(() => { disposed = true; generation++; reads?.abort(); clearProtectedPosters() })
  return { ...toRefs(props), ArrowUpRight, ChevronLeft, ChevronRight, Plus, RefreshCw, loading, error, adding, linked, linkedOptions, candidates,
    preview, previewPosterUrl, previewMotion, targetIndex, targetId, canAdd, previousTarget, nextTarget, selectTarget, load, addToCompetition }
}
export type ChallengeCompetitionPlacementsViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useChallengeCompetitionPlacements>>
