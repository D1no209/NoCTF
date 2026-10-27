import type { NoCtfapiEndpointsLiveSoloLiveSoloDirectionResponse as Direction } from '../../api'

export function directionPoolCounts(pool: readonly Pick<Direction, 'direction' | 'available' | 'drawnAt'>[]) {
  const counts = new Map<string, number>()
  for (const entry of pool) {
    const direction = entry.direction ?? ''
    counts.set(direction, (counts.get(direction) ?? 0) + (entry.available && !entry.drawnAt ? 1 : 0))
  }
  return Array.from(counts, ([direction, remaining]) => ({ direction, remaining }))
}
