import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import type { Edge, Node } from '@vue-flow/core'
import { getPlayerCompetitionProgression } from '../../../../api'
import type { NoCtfapiEndpointsCompetitionsPlayerProgressionContract } from '../../../../api'
import { buildProgressionLayout as calculateProgressionLayout } from '../../../../lib/progression-layout'
import { progressionTopologyKey } from '../../../../lib/progression-layout'
import { chooseProgressionFocus, unsatisfiedProgressionEdges } from '../../../../lib/progression-graph'
import type { ProgressionLayoutDirection } from '../../../../lib/progression-layout'
import ProgressionCanvasComponent from '~/components/ui/progression/ProgressionCanvas.vue'
import { markRaw, nextTick } from 'vue'

type ReadData = {
  title: string, description?: string | null, kind: 0 | 1, resourceId: string
  active: boolean, complete: boolean, visited: boolean, firstOpenedAt?: string | null
  requiresPrerequisites: boolean
  imageUrl?: string | null
}
type ReadNode = Node<ReadData>
type ReadEdge = Edge<{ condition: 0 | 1 }>

export function useCompetitionsByIdProgressionPage() {
  const route = useRoute()
  const competitionId = route.params.id as string
  const data = ref<NoCtfapiEndpointsCompetitionsPlayerProgressionContract | null>(null)
  const loading = ref(true)
  const error = ref<UiMessage | null>(null)
  const nodes = shallowRef<ReadNode[]>([])
  const edges = shallowRef<ReadEdge[]>([])
  const direction = ref<ProgressionLayoutDirection>('RIGHT')
  const initialFocusNodeId = ref<string | null>(null)
  const selectedNodeId = ref<string | null>(null)
  const badgeSheetOpen = ref(false)
  const selectedBadgeId = ref<string | null>(null)
  const isExpanded = ref(false)
  const highlightedNodeIds = shallowRef<ReadonlySet<string>>(new Set())
  const highlightedEdgeIds = shallowRef<ReadonlySet<string>>(new Set())
  const blockedMessage = ref<string | null>(null)
  let currentTopology = ''
  let loadGeneration = 0
  let blockedTimer: ReturnType<typeof setTimeout> | undefined
  let media: MediaQueryList | undefined

  const selectedNode = computed(() => nodes.value.find(node =>
    node.id === selectedNodeId.value) ?? null)
  const selectedRequirements = computed(() => {
    if (!selectedNode.value) return []
    const byId = new Map(nodes.value.map(node => [node.id, node]))
    return edges.value.filter(edge => edge.target === selectedNode.value!.id)
      .map(edge => {
        const predecessor = byId.get(edge.source)
        const condition = edge.data?.condition ?? 0
        return {
          id: edge.id,
          title: predecessor?.data?.title ?? '',
          condition,
          satisfied: condition === 1
            ? predecessor?.data?.complete !== true
            : predecessor?.data?.complete === true,
        }
      })
  })
  const challengeNodes = computed(() => nodes.value.filter(node => node.data?.kind === 0))
  const completedChallenges = computed(() => challengeNodes.value.filter(node => node.data?.complete).length)
  const currentProgressNodeId = computed(() => chooseProgressionFocus(nodes.value.map(node => ({
    id: node.id, kind: node.data!.kind, active: node.data!.active,
    complete: node.data!.complete, visited: node.data!.visited,
    firstOpenedAt: node.data!.firstOpenedAt,
    x: node.position.x, y: node.position.y,
  }))))

  async function load() {
    const generation = ++loadGeneration
    if (!data.value) loading.value = true
    const result = await getPlayerCompetitionProgression({ path: { competitionId } })
    if (generation !== loadGeneration) return
    if (!result.data || result.error) {
      loading.value = false
      error.value = parseApiError(result.error, describeMessage('progression.loadFailed')).displayMessage
      return
    }
    if (result.data.nodes?.some(node => !node.id || !node.resourceId
      || typeof node.requiresPrerequisites !== 'boolean'
      || (node.kind !== 0 && node.kind !== 1))
      || result.data.edges?.some(edge => !edge.id || !edge.sourceNodeId || !edge.targetNodeId
        || (edge.condition !== 0 && edge.condition !== 1))) {
      loading.value = false
      error.value = describeMessage('progression.loadFailed')
      return
    }
    data.value = result.data
    error.value = null
    const incomingNodes: ReadNode[] = (result.data.nodes ?? []).map(node => ({
      id: node.id!, type: 'read-progression', position: { x: 0, y: 0 },
      data: {
        title: node.title ?? '', description: node.description,
        kind: node.kind === 1 ? 1 : 0, resourceId: node.resourceId!,
        active: node.active ?? false, complete: node.complete ?? false,
        visited: node.visited ?? false, firstOpenedAt: node.firstOpenedAt,
        requiresPrerequisites: node.requiresPrerequisites!,
        imageUrl: node.imageUrl,
      },
    }))
    const incomingEdges: ReadEdge[] = (result.data.edges ?? []).map(edge => ({
      id: edge.id!, source: edge.sourceNodeId!, target: edge.targetNodeId!,
      type: 'smoothstep',
      class: edge.condition === 1 ? 'progression-incomplete-edge' : undefined,
      label: edge.condition === 1 ? translate('progression.incomplete') : undefined,
      data: { condition: edge.condition === 1 ? 1 : 0 },
    }))
    const topology = progressionTopologyKey(incomingNodes.map(node => ({
      id: node.id, kind: node.data!.kind,
    })), incomingEdges, direction.value)
    const changed = topology !== currentTopology
    const previousPositions = new Map(nodes.value.map(node => [node.id, node.position]))
    nodes.value = incomingNodes.map(node => ({
      ...node, position: previousPositions.get(node.id) ?? node.position,
    }))
    edges.value = incomingEdges
    if (changed) {
      try {
        const positions = await calculateProgressionLayout(
          incomingNodes.map(node => ({ id: node.id, kind: node.data!.kind,
            width: 240, height: 96 })),
          incomingEdges.map(edge => ({ source: edge.source, target: edge.target })),
          direction.value)
        if (generation !== loadGeneration) return
        const byId = new Map(positions.map(position => [position.id, position]))
        nodes.value = nodes.value.map(node => ({
          ...node, position: byId.get(node.id) ?? node.position,
        }))
        currentTopology = topology
        initialFocusNodeId.value = null
        await nextTick()
        if (generation !== loadGeneration) return
        initialFocusNodeId.value = selectedNodeId.value ?? currentProgressNodeId.value
      }
      catch {
        error.value = describeMessage('progression.layoutFailed')
      }
    }
    loading.value = false
    if (selectedNodeId.value && !nodes.value.some(node => node.id === selectedNodeId.value))
      closeSelectedNode()
    else if (selectedNode.value?.data?.active && blockedMessage.value) {
      blockedMessage.value = null
      highlightedNodeIds.value = new Set()
      highlightedEdgeIds.value = new Set()
    }
    const blockedResource = route.query.blocked
    if (typeof blockedResource === 'string') {
      const blocked = nodes.value.find(node => node.data?.kind === 0
        && node.data.resourceId === blockedResource && !node.data.active)
      if (blocked) {
        selectedNodeId.value = blocked.id
        showBlockers(blocked.id)
      }
    }
  }

  function showBlockers(targetId: string) {
    const sources = new Map(nodes.value.map(node => [node.id, node]))
    const unmet = unsatisfiedProgressionEdges(
      new Map(nodes.value.map(node => [node.id, node.data!.complete])),
      edges.value.map(edge => ({
        id: edge.id, source: edge.source, target: edge.target,
        condition: edge.data?.condition ?? 0,
      })), targetId)
    highlightedNodeIds.value = new Set([targetId, ...unmet.map(edge => edge.source)])
    highlightedEdgeIds.value = new Set(unmet.map(edge => edge.id))
    blockedMessage.value = unmet.length
      ? translate('progression.blockedBy', {
          names: unmet.map(edge => sources.get(edge.source)?.data?.title ?? edge.source).join('、'),
        })
      : translate('progression.startFailed')
    if (blockedTimer) clearTimeout(blockedTimer)
    if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches)
      blockedTimer = setTimeout(() => {
        highlightedNodeIds.value = new Set()
        highlightedEdgeIds.value = new Set()
      }, 3500)
  }

  function closeSelectedNode() {
    selectedNodeId.value = null
    blockedMessage.value = null
    highlightedNodeIds.value = new Set()
    highlightedEdgeIds.value = new Set()
    if (blockedTimer) clearTimeout(blockedTimer)
  }

  function onNodeClick(id: string) {
    const node = nodes.value.find(item => item.id === id)
    if (!node?.data) return
    selectedNodeId.value = id
    if (!node.data.active) {
      showBlockers(id)
      return
    }
    blockedMessage.value = null
    highlightedNodeIds.value = new Set()
    highlightedEdgeIds.value = new Set()
    if (blockedTimer) clearTimeout(blockedTimer)
  }

  async function enterSelectedChallenge() {
    const node = selectedNode.value
    if (!node?.data || node.data.kind !== 0 || !node.data.active) return
    useState<{ competitionId: string, challengeId: string, revision: number } | null>(
      'progression-entry', () => null).value = {
      competitionId, challengeId: node.data.resourceId, revision: data.value?.revision ?? 0,
    }
    await navigateTo(`/competitions/${competitionId}/challenges/${node.data.resourceId}`)
  }

  function toggleExpanded() { isExpanded.value = !isExpanded.value }

  function openBadges() {
    selectedBadgeId.value = null
    badgeSheetOpen.value = true
  }

  function toggleBadgeDetails(badgeId: string) {
    selectedBadgeId.value = selectedBadgeId.value === badgeId ? null : badgeId
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key !== 'Escape' || badgeSheetOpen.value) return
    if (selectedNodeId.value) closeSelectedNode()
    else if (isExpanded.value) isExpanded.value = false
  }

  function onViewportWidthChange() {
    const next = media?.matches ? 'DOWN' : 'RIGHT'
    if (direction.value === next) return
    direction.value = next
    void load()
  }

  let unwatch: (() => void) | undefined
  onMounted(() => {
    document.addEventListener('keydown', onKeyDown)
    media = window.matchMedia('(max-width: 720px)')
    direction.value = media.matches ? 'DOWN' : 'RIGHT'
    media.addEventListener('change', onViewportWidthChange)
    void load()
    unwatch = watchCompetition(competitionId, {
      competitionEventChanged: () => void load(),
      onReconnected: () => void load(),
    })
  })
  onUnmounted(() => {
    loadGeneration++
    document.removeEventListener('keydown', onKeyDown)
    if (blockedTimer) clearTimeout(blockedTimer)
    media?.removeEventListener('change', onViewportWidthChange)
    unwatch?.()
  })
  const ProgressionCanvas = markRaw(ProgressionCanvasComponent)
  return {
    data, loading, error, nodes, edges, direction, initialFocusNodeId,
    currentProgressNodeId, highlightedNodeIds, highlightedEdgeIds,
    blockedMessage, selectedNode, selectedRequirements, challengeNodes,
    completedChallenges, badgeSheetOpen, selectedBadgeId, isExpanded,
    load, onNodeClick, closeSelectedNode, enterSelectedChallenge,
    toggleExpanded, openBadges, toggleBadgeDetails, ProgressionCanvas,
  }
}

export type CompetitionsByIdProgressionPageViewState = import('vue').ShallowUnwrapRef<
  ReturnType<typeof useCompetitionsByIdProgressionPage>>
