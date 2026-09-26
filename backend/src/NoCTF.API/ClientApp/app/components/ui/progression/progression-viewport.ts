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
