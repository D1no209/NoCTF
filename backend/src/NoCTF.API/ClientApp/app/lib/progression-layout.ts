import ELK from 'elkjs/lib/elk-api.js'
import type { ElkNode } from 'elkjs/lib/elk-api.js'
import elkWorkerUrl from 'elkjs/lib/elk-worker.min.js?url'

export type ProgressionLayoutDirection = 'RIGHT' | 'DOWN'
export interface ProgressionLayoutNode { id: string, kind: 0 | 1 }
export interface ProgressionLayoutEdge { source: string, target: string }
export interface ProgressionPosition { id: string, x: number, y: number }

async function layoutWithWorker(graph: ElkNode): Promise<ElkNode> {
  const elk = new ELK({ workerFactory: () => new Worker(elkWorkerUrl) })
  try {
    return await elk.layout(graph)
  }
  finally {
    elk.terminateWorker()
  }
}

export function progressionTopologyKey(
  nodes: readonly ProgressionLayoutNode[], edges: readonly ProgressionLayoutEdge[],
  direction: ProgressionLayoutDirection,
): string {
  const nodeIds = nodes.map(node => node.id).sort()
  const connections = edges.map(edge => `${edge.source}>${edge.target}`).sort()
  return JSON.stringify([direction, nodeIds, connections])
}

export async function buildProgressionLayout(
  nodes: readonly ProgressionLayoutNode[], edges: readonly ProgressionLayoutEdge[],
  direction: ProgressionLayoutDirection,
  layout: (graph: ElkNode) => Promise<ElkNode> = layoutWithWorker,
): Promise<ProgressionPosition[]> {
  if (!nodes.length) return []
  const graph = await layout({
    id: 'progression',
    layoutOptions: {
      'elk.algorithm': 'layered',
      'elk.direction': direction,
      'elk.edgeRouting': 'ORTHOGONAL',
      'elk.separateConnectedComponents': 'true',
      'elk.spacing.componentComponent': '112',
      'elk.spacing.nodeNode': '56',
      'elk.layered.spacing.nodeNodeBetweenLayers': '110',
      'elk.randomizationSeed': '1',
    },
    children: [...nodes].sort((a, b) => a.id.localeCompare(b.id)).map(node => ({
      id: node.id, width: 200, height: node.kind === 1 ? 88 : 76,
    })),
    edges: [...edges].sort((a, b) =>
      a.source.localeCompare(b.source) || a.target.localeCompare(b.target))
      .map(edge => ({
        id: `${edge.source}>${edge.target}`,
        sources: [edge.source], targets: [edge.target],
      })),
  })
  return (graph.children ?? []).map(node => ({
    id: node.id, x: node.x ?? 0, y: node.y ?? 0,
  }))
}
