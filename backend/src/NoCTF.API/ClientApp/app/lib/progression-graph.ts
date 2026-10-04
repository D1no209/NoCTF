import { dateTimestamp } from '../utils/date-value'
export interface ProgressionConnection {
  source: string
  target: string
}

export type ProgressionConnectionIssue = 'missing' | 'self' | 'duplicate' | 'cycle' | 'limit'
export const maximumProgressionEdges = 4096

export interface ProgressionFocusCandidate {
  id: string
  kind: 0 | 1
  active: boolean
  complete: boolean
  visited: boolean
  firstOpenedAt?: Date | string | null
  x: number
  y: number
}

export function chooseProgressionFocus(nodes: readonly ProgressionFocusCandidate[]): string | null {
  const challenges = nodes.filter(node => node.kind === 0)
  const available = challenges.filter(node => node.active && !node.complete)
  const visited = available.filter(node => node.visited)
    .sort((a, b) => (dateTimestamp(b.firstOpenedAt ?? '') || 0)
      - (dateTimestamp(a.firstOpenedAt ?? '') || 0) || a.id.localeCompare(b.id))
  return visited[0]?.id ?? [...available].sort((a, b) =>
    a.x - b.x || a.y - b.y || a.id.localeCompare(b.id))[0]?.id
    ?? [...challenges].sort((a, b) => a.id.localeCompare(b.id))[0]?.id
    ?? nodes[0]?.id ?? null
}

export interface ProgressionConditionalEdge extends ProgressionConnection {
  id: string
  condition: 0 | 1
}

export function unsatisfiedProgressionEdges(
  completeByNodeId: ReadonlyMap<string, boolean>,
  edges: readonly ProgressionConditionalEdge[], targetId: string,
): ProgressionConditionalEdge[] {
  return edges.filter(edge => edge.target === targetId
    && (edge.condition === 1
      ? completeByNodeId.get(edge.source) === true
      : completeByNodeId.get(edge.source) !== true))
}

interface SelectableElement {
  id: string
}

interface SelectableConnection extends ProgressionConnection, SelectableElement {}

export function getProgressionSelection<NodeType extends SelectableElement, EdgeType extends SelectableConnection>(
  nodes: readonly NodeType[], edges: readonly EdgeType[],
  selectedNodeIds: ReadonlySet<string>, selectedEdgeIds: ReadonlySet<string>,
) {
  const selectedNodes = nodes.filter(node => selectedNodeIds.has(node.id))
  const selectedEdges = edges.filter(edge => selectedEdgeIds.has(edge.id))
  const count = selectedNodes.length + selectedEdges.length
  return {
    selectedNodes,
    selectedEdges,
    count,
    node: count === 1 ? selectedNodes[0] ?? null : null,
    edge: count === 1 ? selectedEdges[0] ?? null : null,
  }
}

export function removeProgressionSelection<NodeType extends SelectableElement, EdgeType extends SelectableConnection>(
  nodes: readonly NodeType[], edges: readonly EdgeType[],
  selectedNodeIds: ReadonlySet<string>, selectedEdgeIds: ReadonlySet<string>,
) {
  return {
    nodes: nodes.filter(node => !selectedNodeIds.has(node.id)),
    edges: edges.filter(edge => !selectedEdgeIds.has(edge.id)
      && !selectedNodeIds.has(edge.source) && !selectedNodeIds.has(edge.target)),
  }
}

export interface ProgressionSelectionChange {
  id: string
  selected: boolean
}

export function applyProgressionSelectionChanges(
  current: ReadonlySet<string>, changes: readonly ProgressionSelectionChange[],
): Set<string> {
  const next = new Set(current)
  for (const change of changes) {
    if (change.selected) next.add(change.id)
    else next.delete(change.id)
  }
  return next
}

export function updateProgressionNodePositions<
  NodeType extends SelectableElement & { position: { x: number, y: number } },
>(nodes: readonly NodeType[], positions: readonly { id: string, x: number, y: number }[]): NodeType[] {
  const byId = new Map(positions.map(position => [position.id, position]))
  return nodes.map(node => {
    const position = byId.get(node.id)
    return position ? { ...node, position: { x: position.x, y: position.y } } : node
  })
}

/** Mirrors the server's cycle, self-reference and duplicate-edge checks for draft editing. */
export function canConnectProgression(
  edges: readonly ProgressionConnection[], source: string, target: string,
): boolean {
  return progressionConnectionIssue(edges, source, target) === null
}

export function progressionConnectionIssue(
  edges: readonly ProgressionConnection[], source: string, target: string,
): ProgressionConnectionIssue | null {
  if (!source || !target) return 'missing'
  if (source === target) return 'self'
  if (edges.some(edge => edge.source === source && edge.target === target))
    return 'duplicate'
  if (edges.length >= maximumProgressionEdges) return 'limit'

  const seen = new Set<string>()
  const reachesSource = (current: string): boolean => {
    if (current === source) return true
    if (seen.has(current)) return false
    seen.add(current)
    return edges.some(edge => edge.source === current && reachesSource(edge.target))
  }
  return reachesSource(target) ? 'cycle' : null
}

export function progressionBatchIssue(
  edges: readonly ProgressionConnection[], source: string,
  targets: readonly string[],
): ProgressionConnectionIssue | null {
  const checked = [...edges]
  for (const target of targets) {
    const issue = progressionConnectionIssue(checked, source, target)
    if (issue) return issue
    checked.push({ source, target })
  }
  return null
}
