import type { Connection, Edge, Node } from '@vue-flow/core'
import { toast } from 'vue-sonner'
import {
  adminCreateCompetitionBadge,
  adminDeleteCompetitionBadge,
  adminGetCompetitionProgression,
  adminListCompetitionBadges,
  adminListCompetitionChallenges,
  adminSaveCompetitionProgression,
  adminUpdateCompetitionBadge,
} from '../../../../../api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionBadgeContract,
  NoCtfapiEndpointsChallengesChallengeSummaryResponse,
} from '../../../../../api'
import { useCompetitionAdmin } from '../../../../../lib/admin-competition'
import {
  applyProgressionSelectionChanges,
  canConnectProgression,
  getProgressionSelection,
  removeProgressionSelection,
  updateProgressionNodePositions,
} from '../../../../../lib/progression-graph'
import type { ProgressionSelectionChange } from '../../../../../lib/progression-graph'
import { directionKey, directionLabel } from '../../../../../utils/directions'
import ProgressionCanvasComponent from '~/components/ui/progression/ProgressionCanvas.vue'
import { markRaw } from 'vue'

type CanvasData = { kind: 0 | 1, resourceId: string, title: string, imageUrl?: string }
type GateData = { condition: 0 | 1 }
type CanvasNode = Node<CanvasData>
type GateEdge = Edge<GateData>

/** A local draft; only Save writes the graph and its immediately effective rule revision. */
export function useAdminCompetitionsByIdProgressionPage() {
  const { competitionId, canWrite, competition } = useCompetitionAdmin()
  const enabled = ref(false)
  const showPlayerMap = ref(false)
  const stamp = ref<string | null>(null)
  const nodes = shallowRef<CanvasNode[]>([])
  const edges = shallowRef<GateEdge[]>([])
  const selectedNodeIds = shallowRef<Set<string>>(new Set())
  const selectedEdgeIds = shallowRef<Set<string>>(new Set())
  const badges = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionBadgeContract[]>([])
  const challenges = ref<NoCtfapiEndpointsChallengesChallengeSummaryResponse[]>([])
  const challengeSearch = ref('')
  const challengeDirection = ref('all')
  const loading = ref(true)
  const saving = ref(false)
  const badgeSaving = ref(false)
  const error = ref<string | null>(null)
  const newBadgeName = ref('')
  const newBadgeDescription = ref('')
  const newBadgeImage = ref<File | null>(null)
  const editingBadgeId = ref<string | null>(null)
  const editBadgeName = ref('')
  const editBadgeDescription = ref('')
  const editBadgeImage = ref<File | null>(null)

  const selection = computed(() => getProgressionSelection(
    nodes.value, edges.value, selectedNodeIds.value, selectedEdgeIds.value))
  const selectedEdge = computed(() => selection.value.edge)
  const selectedNode = computed(() => selection.value.node)
  const selectedCount = computed(() => selection.value.count)
  const availableChallenges = computed(() => challenges.value.filter(challenge =>
    !nodes.value.some(node => node.data?.kind === 0 && node.data.resourceId === challenge.id),
  ))
  const challengeDirections = computed(() => [...new Set(challenges.value
    .map(challenge => directionKey(challenge.direction))
    .filter(Boolean))]
    .map(value => ({ value, label: directionLabel(value) }))
    .sort((left, right) => left.label.localeCompare(right.label)))
  const filteredChallenges = computed(() => {
    const keyword = challengeSearch.value.trim().toLocaleLowerCase()
    return availableChallenges.value.filter(challenge =>
      (challengeDirection.value === 'all'
        || directionKey(challenge.direction) === challengeDirection.value)
      && (!keyword || [challenge.customTitle, challenge.title]
        .some(title => title?.toLocaleLowerCase().includes(keyword))))
  })

  async function load() {
    loading.value = true
    const [graphResult, badgeResult, challengeResult] = await Promise.all([
      adminGetCompetitionProgression({ path: { competitionId } }),
      adminListCompetitionBadges({ path: { competitionId } }),
      adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
    ])
    loading.value = false
    if (!graphResult.data || !badgeResult.data || !challengeResult.data) {
      error.value = translate('progression.loadFailed')
      return
    }
    badges.value = badgeResult.data.items ?? []
    challenges.value = challengeResult.data.items ?? []
    const graph = graphResult.data
    enabled.value = graph.enabled ?? false
    showPlayerMap.value = graph.showPlayerMap ?? false
    stamp.value = graph.concurrencyStamp ?? null
    nodes.value = (graph.nodes ?? []).map(node => {
      const kind = node.kind === 1 ? 1 : 0
      const resourceId = kind === 0
        ? node.challenge?.competitionChallengeId ?? ''
        : node.badge?.competitionBadgeId ?? ''
      const badge = badges.value.find(item => item.id === resourceId)
      const challenge = challenges.value.find(item => item.id === resourceId)
      return {
        id: node.id ?? crypto.randomUUID(),
        type: 'progression',
        position: { x: node.positionX ?? 0, y: node.positionY ?? 0 },
        data: {
          kind,
          resourceId,
          title: kind === 0 ? (challenge?.customTitle || challenge?.title || resourceId) : (badge?.name || resourceId),
          imageUrl: badge?.imageUrl,
        },
      }
    })
    edges.value = (graph.edges ?? []).map(edge => ({
      id: edge.id ?? crypto.randomUUID(),
      source: edge.sourceNodeId ?? '', target: edge.targetNodeId ?? '',
      type: 'smoothstep',
      label: edge.condition === 1 ? translate('progression.incomplete') : translate('progression.completed'),
      data: { condition: edge.condition === 1 ? 1 : 0 },
    }))
    selectedNodeIds.value = new Set()
    selectedEdgeIds.value = new Set()
    error.value = null
  }

  function addChallenge(challenge: NoCtfapiEndpointsChallengesChallengeSummaryResponse) {
    if (!challenge.id || nodes.value.some(node => node.data?.kind === 0 && node.data.resourceId === challenge.id))
      return
    nodes.value = [...nodes.value, {
      id: crypto.randomUUID(), type: 'progression',
      position: { x: 60 + nodes.value.length * 35, y: 60 + nodes.value.length * 30 },
      data: { kind: 0, resourceId: challenge.id, title: challenge.customTitle || challenge.title || challenge.id },
    }]
  }

  function addBadge(badge: NoCtfapiEndpointsAdministrationCompetitionsCompetitionBadgeContract) {
    if (!badge.id) return
    nodes.value = [...nodes.value, {
      id: crypto.randomUUID(), type: 'progression',
      position: { x: 100 + nodes.value.length * 35, y: 80 + nodes.value.length * 30 },
      data: { kind: 1, resourceId: badge.id, title: badge.name ?? badge.id, imageUrl: badge.imageUrl },
    }]
  }

  function connect(connection: Connection) {
    const source = connection.source
    const target = connection.target
    if (!canConnectProgression(edges.value, source, target)) {
      toast.error(translate('progression.invalidConnection'))
      return
    }
    const edge: GateEdge = {
      id: crypto.randomUUID(), source, target, type: 'smoothstep',
      label: translate('progression.completed'), data: { condition: 0 },
    }
    edges.value = [...edges.value, edge]
  }

  function removeSelected() {
    if (!canWrite.value || !selectedCount.value) return
    const remaining = removeProgressionSelection(
      nodes.value, edges.value, selectedNodeIds.value, selectedEdgeIds.value)
    nodes.value = remaining.nodes
    edges.value = remaining.edges
    selectedNodeIds.value = new Set()
    selectedEdgeIds.value = new Set()
  }
  function changeNodeSelection(changes: ProgressionSelectionChange[]) {
    selectedNodeIds.value = applyProgressionSelectionChanges(selectedNodeIds.value, changes)
  }
  function changeEdgeSelection(changes: ProgressionSelectionChange[]) {
    selectedEdgeIds.value = applyProgressionSelectionChanges(selectedEdgeIds.value, changes)
  }
  function updateNodePositions(positions: { id: string, x: number, y: number }[]) {
    nodes.value = updateProgressionNodePositions(nodes.value, positions)
  }
  function setEdgeCondition(condition: 0 | 1) {
    const edge = selectedEdge.value
    if (!edge) return
    edges.value = edges.value.map(item => item.id === edge.id
      ? { ...item, data: { condition }, label: translate(condition === 0 ? 'progression.completed' : 'progression.incomplete') }
      : item)
  }

  async function save() {
    if (!canWrite.value || saving.value) return
    saving.value = true
    const { data, error: saveError } = await adminSaveCompetitionProgression({
      path: { competitionId },
      body: {
        expectedConcurrencyStamp: stamp.value,
        enabled: enabled.value, showPlayerMap: showPlayerMap.value,
        nodes: nodes.value.map(node => ({
          id: node.id, kind: node.data!.kind,
          challenge: node.data!.kind === 0 ? { competitionChallengeId: node.data!.resourceId } : null,
          badge: node.data!.kind === 1 ? { competitionBadgeId: node.data!.resourceId } : null,
          positionX: node.position.x, positionY: node.position.y,
        })),
        edges: edges.value.map(edge => ({
          id: edge.id, sourceNodeId: edge.source,
          targetNodeId: edge.target, condition: edge.data?.condition ?? 0,
        })),
      },
    })
    saving.value = false
    if (!data || saveError) {
      error.value = parseApiError(saveError, translate('progression.saveFailed')).message
      return
    }
    stamp.value = data.concurrencyStamp ?? null
    error.value = null
    toast.success(translate('progression.saved'))
  }

  function onBadgeFileChange(event: Event) {
    newBadgeImage.value = (event.target as HTMLInputElement).files?.[0] ?? null
  }

  function onEditBadgeFileChange(event: Event) {
    editBadgeImage.value = (event.target as HTMLInputElement).files?.[0] ?? null
  }

  function beginEditBadge(badge: NoCtfapiEndpointsAdministrationCompetitionsCompetitionBadgeContract) {
    editingBadgeId.value = badge.id ?? null
    editBadgeName.value = badge.name ?? ''
    editBadgeDescription.value = badge.description ?? ''
    editBadgeImage.value = null
  }

  async function updateBadge() {
    if (!canWrite.value || !editingBadgeId.value || !editBadgeName.value.trim()) return
    badgeSaving.value = true
    const { data, error: updateError } = await adminUpdateCompetitionBadge({
      path: { competitionId, badgeId: editingBadgeId.value },
      body: {
        name: editBadgeName.value.trim(),
        description: editBadgeDescription.value.trim() || null,
        image: editBadgeImage.value,
      },
    })
    badgeSaving.value = false
    if (!data || updateError) {
      error.value = parseApiError(updateError, translate('progression.badgeUpdateFailed')).message
      return
    }
    badges.value = badges.value.map(badge => badge.id === data.id ? data : badge)
    nodes.value = nodes.value.map(node => node.data?.kind === 1 && node.data.resourceId === data.id
      ? { ...node, data: { ...node.data, title: data.name ?? '', imageUrl: data.imageUrl } }
      : node)
    editingBadgeId.value = null
    editBadgeImage.value = null
    toast.success(translate('progression.badgeUpdated'))
  }

  async function createBadge() {
    if (!canWrite.value || !newBadgeName.value.trim() || !newBadgeImage.value) return
    badgeSaving.value = true
    const { data, error: createError } = await adminCreateCompetitionBadge({
      path: { competitionId },
      body: {
        name: newBadgeName.value.trim(),
        description: newBadgeDescription.value.trim() || null,
        image: newBadgeImage.value,
      },
    })
    badgeSaving.value = false
    if (!data || createError) {
      error.value = parseApiError(createError, translate('progression.badgeCreateFailed')).message
      return
    }
    badges.value.push(data)
    newBadgeName.value = ''
    newBadgeDescription.value = ''
    newBadgeImage.value = null
    toast.success(translate('progression.badgeCreated'))
  }

  async function deleteBadge(badgeId: string) {
    if (!canWrite.value || nodes.value.some(node => node.data?.kind === 1 && node.data.resourceId === badgeId)) {
      toast.error(translate('progression.badgeInUse'))
      return
    }
    const { error: deleteError } = await adminDeleteCompetitionBadge({
      path: { competitionId, badgeId },
    })
    if (deleteError) {
      error.value = parseApiError(deleteError, translate('progression.badgeDeleteFailed')).message
      return
    }
    badges.value = badges.value.filter(badge => badge.id !== badgeId)
  }

  onMounted(load)
  const ProgressionCanvas = markRaw(ProgressionCanvasComponent)
  return {
    competition, canWrite, enabled, showPlayerMap, nodes, edges, badges,
    challenges, availableChallenges, filteredChallenges, challengeSearch,
    challengeDirection, challengeDirections, loading, saving, badgeSaving, error,
    selectedNode, selectedEdge, selectedCount, newBadgeName, newBadgeDescription, newBadgeImage,
    editingBadgeId, editBadgeName, editBadgeDescription, editBadgeImage,
    load, addChallenge, addBadge, connect, removeSelected, changeNodeSelection,
    changeEdgeSelection, updateNodePositions, setEdgeCondition, save,
    onBadgeFileChange, createBadge, deleteBadge,
    onEditBadgeFileChange, beginEditBadge, updateBadge,
    ProgressionCanvas,
  }
}

export type AdminCompetitionsByIdProgressionPageViewState = import('vue').ShallowUnwrapRef<
  ReturnType<typeof useAdminCompetitionsByIdProgressionPage>>
