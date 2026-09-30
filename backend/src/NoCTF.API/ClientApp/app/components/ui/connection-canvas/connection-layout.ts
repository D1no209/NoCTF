export interface ConnectionNode {
  id: string
  label: string
  detail?: string
  column: 0 | 1
  selectable?: boolean
  invalid?: boolean
}

export interface ConnectionEdge { source: string; target: string }
export interface PositionedConnectionNode extends ConnectionNode { x: number; y: number; width: number; height: number }

export function layoutConnections(nodes: readonly ConnectionNode[], availableWidth: number) {
  const width = Math.max(480, availableWidth)
  const columns = [nodes.filter(node => node.column === 0), nodes.filter(node => node.column === 1)]
  const rows = Math.max(1, ...columns.map(column => column.length))
  const height = rows * 80 + 24
  const nodeWidth = (width - 112) / 2
  const positioned: PositionedConnectionNode[] = columns.flatMap((column, side) => column.map((node, index) => ({
    ...node,
    x: side === 0 ? 16 : width - nodeWidth - 16,
    y: 12 + (rows - column.length) * 40 + index * 80,
    width: nodeWidth,
    height: 60,
  })))
  return { width, height, nodes: positioned }
}
