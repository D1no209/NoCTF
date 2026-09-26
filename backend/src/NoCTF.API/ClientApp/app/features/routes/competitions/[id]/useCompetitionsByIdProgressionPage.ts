import type { Edge, Node } from '@vue-flow/core'
import { getPlayerCompetitionProgression } from '../../../../api'
import type {
  NoCtfapiEndpointsCompetitionsPlayerProgressionContract,
} from '../../../../api'
import ProgressionCanvasComponent from '~/components/ui/progression/ProgressionCanvas.vue'
import { markRaw } from 'vue'

type ReadNode = Node<{ title: string, kind: 0 | 1, active: boolean, complete: boolean, imageUrl?: string | null }>

export function useCompetitionsByIdProgressionPage() {
  const route = useRoute()
  const competitionId = route.params.id as string
  const data = ref<NoCtfapiEndpointsCompetitionsPlayerProgressionContract | null>(null)
  const loading = ref(true)
  const error = ref<string | null>(null)
  const nodes = shallowRef<ReadNode[]>([])
  const edges = shallowRef<Edge[]>([])

  async function load() {
    loading.value = true
    const result = await getPlayerCompetitionProgression({ path: { competitionId } })
    loading.value = false
    if (!result.data || result.error) {
      error.value = parseApiError(result.error, translate('progression.loadFailed')).message
      return
    }
    data.value = result.data
    error.value = null
    nodes.value = (result.data.nodes ?? []).map(node => ({
      id: node.id ?? crypto.randomUUID(),
      type: 'read-progression',
      position: { x: node.positionX ?? 0, y: node.positionY ?? 0 },
      data: {
        title: node.title ?? '', kind: node.kind === 1 ? 1 : 0,
        active: node.active ?? false, complete: node.complete ?? false,
        imageUrl: node.imageUrl,
      },
    }))
    edges.value = (result.data.edges ?? []).map(edge => ({
      id: edge.id ?? crypto.randomUUID(),
      source: edge.sourceNodeId ?? '', target: edge.targetNodeId ?? '',
      type: 'smoothstep',
      label: translate(edge.condition === 1 ? 'progression.incomplete' : 'progression.completed'),
    }))
  }

  let unwatch: (() => void) | undefined
  onMounted(() => {
    void load()
    unwatch = watchCompetition(competitionId, {
      competitionEventChanged: () => void load(),
      onReconnected: () => void load(),
    })
  })
  onUnmounted(() => unwatch?.())
  const ProgressionCanvas = markRaw(ProgressionCanvasComponent)
  return { data, loading, error, nodes, edges, load, ProgressionCanvas }
}

export type CompetitionsByIdProgressionPageViewState = import('vue').ShallowUnwrapRef<
  ReturnType<typeof useCompetitionsByIdProgressionPage>>
