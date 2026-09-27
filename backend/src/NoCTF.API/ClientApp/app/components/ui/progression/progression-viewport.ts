export interface ProgressionViewport {
  fitView(options: { padding: number, minZoom: number, maxZoom: number }): Promise<boolean>
  setViewport(transform: { x: number, y: number, zoom: number }): Promise<boolean>
}

/** Reframe the diagram without changing any persisted node position. */
export function restoreProgressionViewport(viewport: ProgressionViewport, hasNodes: boolean): Promise<boolean> {
  return hasNodes
    ? viewport.fitView({ padding: 0.2, minZoom: 0.001, maxZoom: 1 })
    : viewport.setViewport({ x: 0, y: 0, zoom: 1 })
}

export function progressionFocusTransform(
  node: { x: number, y: number, width: number, height: number },
  viewport: { width: number, height: number },
  direction: 'RIGHT' | 'DOWN',
) {
  const anchorX = direction === 'DOWN' ? 0.5 : 0.3
  const anchorY = direction === 'DOWN' ? 0.3 : 0.5
  return {
    x: viewport.width * anchorX - node.x - node.width / 2,
    y: viewport.height * anchorY - node.y - node.height / 2,
    zoom: 1,
  }
}
