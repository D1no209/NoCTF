export interface ProgressionConnection {
  source: string
  target: string
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
  if (!source || !target || source === target
    || edges.some(edge => edge.source === source && edge.target === target))
    return false

  const seen = new Set<string>()
  const reachesSource = (current: string): boolean => {
    if (current === source) return true
    if (seen.has(current)) return false
    seen.add(current)
    return edges.some(edge => edge.source === current && reachesSource(edge.target))
  }
  return !reachesSource(target)
}
