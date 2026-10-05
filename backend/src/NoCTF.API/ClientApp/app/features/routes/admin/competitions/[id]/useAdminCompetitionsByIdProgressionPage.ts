import { message as describeMessage } from '../../../../../utils/i18n'
import type { UiMessage } from '../../../../../utils/i18n'
import type { Connection, Edge, Node } from '@vue-flow/core'
import { toast } from '../../../../../utils/message-toast'
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
  maximumProgressionEdges,
  progressionBatchIssue,
  progressionConnectionIssue,
  removeProgressionSelection,
  updateProgressionNodePositions,
} from '../../../../../lib/progression-graph'
import type { ProgressionSelectionChange } from '../../../../../lib/progression-graph'
import { buildProgressionLayout as calculateProgressionLayout } from '../../../../../lib/progression-layout'
import { directionKey } from '../../../../../utils/directions'
import ProgressionCanvasComponent from '~/components/ui/progression/ProgressionCanvas.vue'
import { markRaw } from 'vue'

type CanvasData = { kind: 0 | 1, resourceId: string, title: string, requiresPrerequisites: boolean, imageUrl?: string }
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
  const batch = shallowRef<{ sourceId: string, condition: 0 | 1, targets: Set<string> } | null>(null)
  const layoutRevision = ref(0)
  let layoutGeneration = 0
  const badges = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionBadgeContract[]>([])
  const challenges = ref<NoCtfapiEndpointsChallengesChallengeSummaryResponse[]>([])
  const challengeSearch = ref('')
  const challengeDirection = ref('all')
  const loading = ref(true)
  const saving = ref(false)
  const badgeSaving = ref(false)
  const error = ref<UiMessage | null>(null)
  const newBadgeName = ref('')
  const newBadgeDescription = ref('')
  const newBadgeImage = ref<File | null>(null)
  const newBadgeUploadKey = ref(0)
  const editingBadgeId = ref<string | null>(null)
  const editBadgeName = ref('')
  const editBadgeDescription = ref('')
  const editBadgeImage = ref<File | null>(null)
  const editBadgeUploadKey = ref(0)

  const selection = computed(() => getProgressionSelection(
    nodes.value, edges.value, selectedNodeIds.value, selectedEdgeIds.value))
  const selectedEdge = computed(() => selection.value.edge)
  const selectedNode = computed(() => selection.value.node)
  const selectedCount = computed(() => selection.value.count)
  const batchTargets = computed(() => batch.value?.targets ?? new Set<string>())
  const batchSourceId = computed(() => batch.value?.sourceId ?? null)
  const batchCondition = computed(() => batch.value?.condition ?? 0)
  const batchDisabledReasons = computed(() => {
    const reasons: Record<string, string> = {}
    if (!batch.value) return reasons
    for (const node of nodes.value) {
      if (batch.value.targets.has(node.id)) continue
      const issue = progressionConnectionIssue(edges.value, batch.value.sourceId, node.id)
        ?? (edges.value.length + batch.value.targets.size >= maximumProgressionEdges ? 'limit' : null)
      if (issue) reasons[node.id] = translate(`progression.connectionIssue.${issue}`)
    }
    return reasons
  })
  const previewEdges = computed<GateEdge[]>(() => !batch.value ? []
    : [...batch.value.targets].map(target => ({
      id: `preview-${batch.value!.sourceId}-${target}`,
      source: batch.value!.sourceId, target,
      type: 'smoothstep', selectable: false,
      class: 'progression-preview-edge',
      label: batch.value!.condition === 1 ? translate('progression.incomplete') : undefined,
      data: { condition: batch.value!.condition },
    })))
  const availableChallenges = computed(() => challenges.value.filter(challenge =>
    !nodes.value.some(node => node.data?.kind === 0 && node.data.resourceId === challenge.id),
  ))
  const challengeDirections = computed(() => [...new Set(challenges.value
    .map(challenge => directionKey(challenge.direction))
    .filter(Boolean))]
    .map(value => ({ value, label: value }))
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
    cancelBatch()
    const currentLayout = ++layoutGeneration
    loading.value = true
    const [graphResult, badgeResult, challengeResult] = await Promise.all([
      adminGetCompetitionProgression({ path: { competitionId } }),
      adminListCompetitionBadges({ path: { competitionId } }),
      adminListCompetitionChallenges({ path: { competitionId }, query: { includeDeleted: false } }),
    ])
    if (!graphResult.data || !badgeResult.data || !challengeResult.data) {
      loading.value = false
      error.value = describeMessage('progression.loadFailed')
      return
    }
    badges.value = badgeResult.data.items ?? []
    challenges.value = challengeResult.data.items ?? []
    const graph = graphResult.data
    if (graph.nodes?.some(node => !node.id || (node.kind !== 0 && node.kind !== 1)
      || typeof node.requiresPrerequisites !== 'boolean'
      || !(node.kind === 0 ? node.challenge?.competitionChallengeId : node.badge?.competitionBadgeId))
      || graph.edges?.some(edge => !edge.id || !edge.sourceNodeId || !edge.targetNodeId
        || (edge.condition !== 0 && edge.condition !== 1))) {
      loading.value = false
      error.value = describeMessage('progression.loadFailed')
      return
    }
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
        id: node.id!,
        type: 'progression',
        position: { x: 0, y: 0 },
        data: {
          kind,
          resourceId,
          requiresPrerequisites: node.requiresPrerequisites!,
          title: kind === 0 ? (challenge?.customTitle || challenge?.title || resourceId) : (badge?.name || resourceId),
          imageUrl: badge?.imageUrl,
        },
      }
    })
    edges.value = (graph.edges ?? []).map(edge => ({
      id: edge.id!,
      source: edge.sourceNodeId!, target: edge.targetNodeId!,
      type: 'smoothstep',
      label: edge.condition === 1 ? translate('progression.incomplete') : undefined,
      data: { condition: edge.condition === 1 ? 1 : 0 },
    }))
    selectedNodeIds.value = new Set()
    selectedEdgeIds.value = new Set()
    error.value = null
    await arrange(currentLayout)
    loading.value = false
  }

  async function arrange(expectedGeneration?: number) {
    if (batch.value || !nodes.value.length) return
    const generation = expectedGeneration ?? ++layoutGeneration
    try {
      const positions = await calculateProgressionLayout(
        nodes.value.map(node => ({ id: node.id, kind: node.data!.kind })),
        edges.value.map(edge => ({ source: edge.source, target: edge.target })), 'RIGHT')
      if (generation !== layoutGeneration || batch.value) return
      nodes.value = updateProgressionNodePositions(nodes.value, positions)
      layoutRevision.value++
    }
    catch {
      toast.error(describeMessage('progression.layoutFailed'))
    }
  }

  function autoArrange() { return arrange() }

  function nextNodePosition() {
    return {
      x: nodes.value.length ? Math.max(...nodes.value.map(node => node.position.x)) + 280 : 60,
      y: 60,
    }
  }

  function addChallenge(challenge: NoCtfapiEndpointsChallengesChallengeSummaryResponse) {
    if (batch.value || !challenge.id || nodes.value.some(node => node.data?.kind === 0 && node.data.resourceId === challenge.id))
      return
    layoutGeneration++
    nodes.value = [...nodes.value, {
      id: crypto.randomUUID(), type: 'progression',
      position: nextNodePosition(),
      data: { kind: 0, resourceId: challenge.id, title: challenge.customTitle || challenge.title || challenge.id,
        requiresPrerequisites: true },
    }]
  }

  function addBadge(badge: NoCtfapiEndpointsAdministrationCompetitionsCompetitionBadgeContract) {
    if (batch.value || !badge.id) return
    layoutGeneration++
    nodes.value = [...nodes.value, {
      id: crypto.randomUUID(), type: 'progression',
      position: nextNodePosition(),
      data: { kind: 1, resourceId: badge.id, title: badge.name ?? badge.id, imageUrl: badge.imageUrl,
        requiresPrerequisites: true },
    }]
  }

  function connect(connection: Connection) {
    if (batch.value) return
    const source = connection.source
    const target = connection.target
    if (!canConnectProgression(edges.value, source, target)) {
      toast.error(describeMessage('progression.invalidConnection'))
      return
    }
    const edge: GateEdge = {
      id: crypto.randomUUID(), source, target, type: 'smoothstep',
      data: { condition: 0 },
    }
    edges.value = [...edges.value, edge]
  }

  function removeSelected() {
    if (batch.value || !canWrite.value || !selectedCount.value) return
    layoutGeneration++
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
    if (batch.value) return
    layoutGeneration++
    nodes.value = updateProgressionNodePositions(nodes.value, positions)
  }
  function setEdgeCondition(condition: 0 | 1) {
    const edge = selectedEdge.value
    if (!edge || batch.value) return
    edges.value = edges.value.map(item => item.id === edge.id
      ? { ...item, data: { condition }, label: condition === 1 ? translate('progression.incomplete') : undefined }
      : item)
  }

  function setSelectedNodeRequiresPrerequisites(value: boolean) {
    const selected = selectedNode.value
    if (!canWrite.value || !selected || batch.value) return
    nodes.value = nodes.value.map(node => node.id === selected.id
      ? { ...node, data: { ...node.data!, requiresPrerequisites: value } }
      : node)
  }

  function beginBatch() {
    if (!canWrite.value || !selectedNode.value || batch.value) return
    batch.value = { sourceId: selectedNode.value.id, condition: 0, targets: new Set() }
  }

  function cancelBatch() { batch.value = null }

  function setBatchCondition(condition: 0 | 1) {
    if (batch.value) batch.value = { ...batch.value, condition }
  }

  function toggleBatchTarget(targetId: string) {
    if (!batch.value || !nodes.value.some(node => node.id === targetId)) return
    const targets = new Set(batch.value.targets)
    if (targets.has(targetId)) targets.delete(targetId)
    else {
      const reason = batchDisabledReasons.value[targetId]
      if (reason) { toast.error(reason); return }
      targets.add(targetId)
    }
    batch.value = { ...batch.value, targets }
  }

  function applyBatch() {
    const currentBatch = batch.value
    if (!currentBatch || !currentBatch.targets.size || !canWrite.value) return
    const targets = [...currentBatch.targets].sort()
    const issue = progressionBatchIssue(edges.value, currentBatch.sourceId, targets)
    if (issue) {
      toast.error(describeMessage(`progression.connectionIssue.${issue}`))
      return
    }
    const next: GateEdge[] = targets.map(target => ({
        id: crypto.randomUUID(), source: currentBatch.sourceId, target,
        type: 'smoothstep',
        label: currentBatch.condition === 1 ? translate('progression.incomplete') : undefined,
        data: { condition: currentBatch.condition },
      }))
    edges.value = [...edges.value, ...next]
    batch.value = null
  }

  function onKeyDown(event: KeyboardEvent) {
    if (event.key === 'Escape' && batch.value) cancelBatch()
  }

  async function save() {
    if (!canWrite.value || saving.value || batch.value) return
    saving.value = true
    const { data, error: saveError } = await adminSaveCompetitionProgression({
      path: { competitionId },
      body: {
        expectedConcurrencyStamp: stamp.value,
        enabled: enabled.value, showPlayerMap: showPlayerMap.value,
        nodes: nodes.value.map(node => ({
          id: node.id, kind: node.data!.kind,
          requiresPrerequisites: node.data!.requiresPrerequisites,
          challenge: node.data!.kind === 0 ? { competitionChallengeId: node.data!.resourceId } : null,
          badge: node.data!.kind === 1 ? { competitionBadgeId: node.data!.resourceId } : null,
        })),
        edges: edges.value.map(edge => ({
          id: edge.id, sourceNodeId: edge.source,
          targetNodeId: edge.target, condition: edge.data?.condition ?? 0,
        })),
      },
    })
    saving.value = false
    if (!data || saveError) {
      error.value = parseApiError(saveError, describeMessage('progression.saveFailed')).displayMessage
      return
    }
    stamp.value = data.concurrencyStamp ?? null
    error.value = null
    toast.success(describeMessage('progression.saved'))
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
    editBadgeUploadKey.value++
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
      error.value = parseApiError(updateError, describeMessage('progression.badgeUpdateFailed')).displayMessage
      return
    }
    badges.value = badges.value.map(badge => badge.id === data.id ? data : badge)
    nodes.value = nodes.value.map(node => node.data?.kind === 1 && node.data.resourceId === data.id
      ? { ...node, data: { ...node.data, title: data.name ?? '', imageUrl: data.imageUrl } }
      : node)
    editingBadgeId.value = null
    editBadgeImage.value = null
    toast.success(describeMessage('progression.badgeUpdated'))
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
      error.value = parseApiError(createError, describeMessage('progression.badgeCreateFailed')).displayMessage
      return
    }
    badges.value.push(data)
    newBadgeName.value = ''
    newBadgeDescription.value = ''
    newBadgeImage.value = null
    newBadgeUploadKey.value++
    toast.success(describeMessage('progression.badgeCreated'))
  }

  async function deleteBadge(badgeId: string) {
    if (!canWrite.value || nodes.value.some(node => node.data?.kind === 1 && node.data.resourceId === badgeId)) {
      toast.error(describeMessage('progression.badgeInUse'))
      return
    }
    const { error: deleteError } = await adminDeleteCompetitionBadge({
      path: { competitionId, badgeId },
    })
    if (deleteError) {
      error.value = parseApiError(deleteError, describeMessage('progression.badgeDeleteFailed')).displayMessage
      return
    }
    badges.value = badges.value.filter(badge => badge.id !== badgeId)
  }

  onMounted(() => {
    document.addEventListener('keydown', onKeyDown)
    void load()
  })
  onBeforeUnmount(() => {
    layoutGeneration++
    document.removeEventListener('keydown', onKeyDown)
  })
  const ProgressionCanvas = markRaw(ProgressionCanvasComponent)
  return {
    competition, canWrite, enabled, showPlayerMap, nodes, edges, badges,
    challenges, availableChallenges, filteredChallenges, challengeSearch,
    challengeDirection, challengeDirections, loading, saving, badgeSaving, error,
    selectedNode, selectedEdge, selectedCount, newBadgeName, newBadgeDescription, newBadgeImage,
    batch, batchTargets, batchSourceId, batchCondition, batchDisabledReasons, previewEdges,
    layoutRevision,
    newBadgeUploadKey, editingBadgeId, editBadgeName, editBadgeDescription, editBadgeImage,
    editBadgeUploadKey,
    load, addChallenge, addBadge, connect, removeSelected, changeNodeSelection,
    changeEdgeSelection, updateNodePositions, setEdgeCondition,
    setSelectedNodeRequiresPrerequisites, save, autoArrange,
    beginBatch, cancelBatch, setBatchCondition, toggleBatchTarget, applyBatch,
    onBadgeFileChange, createBadge, deleteBadge,
    onEditBadgeFileChange, beginEditBadge, updateBadge,
    ProgressionCanvas,
  }
}

export type AdminCompetitionsByIdProgressionPageViewState = import('vue').ShallowUnwrapRef<
  ReturnType<typeof useAdminCompetitionsByIdProgressionPage>>
