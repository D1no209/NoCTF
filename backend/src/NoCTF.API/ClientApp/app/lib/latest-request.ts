export interface LatestRequestGuard {
  begin: () => number
  invalidate: () => void
  isCurrent: (request: number) => boolean
}

export function createLatestRequestGuard(): LatestRequestGuard {
  let generation = 0

  return {
    begin: () => ++generation,
    invalidate: () => { generation += 1 },
    isCurrent: request => request === generation,
  }
}
