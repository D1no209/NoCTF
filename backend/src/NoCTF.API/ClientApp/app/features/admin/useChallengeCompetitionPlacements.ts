
import { api, RequestPolicyOption, binaryResponse } from '../../lib/api'


import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { computed, onBeforeUnmount, onMounted, ref, toRefs, watch } from 'vue'
import { ArrowUpRight, ChevronLeft, ChevronRight, Plus, RefreshCw } from '@lucide/vue'
import { toast } from '../../utils/message-toast'

import type { NoCTFAPIEndpointsCompetitionsGameModeProtocol } from '../../api/models'
import type { ContentSwapPreset } from '../../motion/useContentSwap'
import { parseApiError } from '../../utils/api-error'
import { competitionChallengeConflictMessage } from '../../lib/competition-challenge-conflict'
import { availablePlacementCompetitions, placementManagementPath, projectChallengePlacements, writablePlacementCompetitions } from './challenge-competition-placements'
import type { ChallengeCompetitionPlacement } from './challenge-competition-placements'

export function useChallengeCompetitionPlacements(props: Readonly<{ challengeId: string; mode: NoCTFAPIEndpointsCompetitionsGameModeProtocol; disabled: boolean }>) {
  const { user } = useAuth()
  const items = ref<ChallengeCompetitionPlacement[]>([])
  const loading = ref(true)
  const error = ref<UiMessage | null>(null)
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
      let failure: unknown;
      const data = await api.api.v1.admin.competitions.get({ queryParameters: { includeDeleted: false }, options: [new RequestPolicyOption({ signal: signal })] }).catch(cause => { failure = cause; return undefined });
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
          let failure: unknown;
          const data = await api.api.v1.admin.competitions.byCompetitionId(competition.id).challenges.get({ queryParameters: { includeDeleted: false }, options: [new RequestPolicyOption({ signal: signal })] }).catch(cause => { failure = cause; return undefined });
          if (signal.aborted) return
          if (failure || !data) failures.push(competition.title ?? '')
          else {
            results.push(projectChallengePlacements(competition, data.items ?? [], props.challengeId))
            if (competition.accessMode === 'StaffOnly' && competition.posterUrl) {
              const image = await binaryResponse(responseOptions => api.api.v1.competitions.byCompetitionId(competition.id).poster.get({ options: [new RequestPolicyOption({ signal: signal, cache: 'no-store' }), ...responseOptions] }), 'blob').catch(() => undefined);
              if (image instanceof Blob && image.type.startsWith('image/') && image.size > 0) posterBlobs.set(competition.id, image)
            }
          }
        }
      }))
      if (signal.aborted || request !== generation) return
      clearProtectedPosters()
      protectedPosters.value = Object.fromEntries([...posterBlobs].map(([id, blob]) => [id, URL.createObjectURL(blob)]))
      items.value = results.sort((a, b) => (a.competition.title ?? '').localeCompare(b.competition.title ?? ''))
      if (failures.length) error.value = describeMessage('placements.partialFailure', { competitions: failures.join('、') })
      if (!candidates.value.some(item => item.competition.id === targetId.value)) targetId.value = candidates.value[0]?.competition.id ?? ''
    } catch (failure) {
      if (!signal.aborted && request === generation) error.value = parseApiError(failure, describeMessage('placements.loadFailed')).displayMessage
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
      let failure: unknown;
      await api.api.v1.admin.competitions.byCompetitionId(target.competition.id).challenges.post({ challengeId: props.challengeId, customTitle: null, order: target.nextOrder }).catch(cause => { failure = cause; return undefined });
      if (disposed || actorId !== user.value?.userId) return
      if (failure) {
        error.value = competitionChallengeConflictMessage(failure) ?? parseApiError(failure).displayMessage
        return
      }
      targetId.value = ''
      toast.success(describeMessage('placements.added', { competition: target.competition.title ?? '' }))
      await load()
    } catch (failure) {
      if (!disposed && actorId === user.value?.userId) error.value = parseApiError(failure, describeMessage('placements.addFailed')).displayMessage
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
