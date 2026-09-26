export interface ProgressionConnection {
  source: string
  target: string
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
