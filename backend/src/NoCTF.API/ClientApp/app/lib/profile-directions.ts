import { challengeDirectionOptions, directionKey, directionLabel } from '../utils/directions'

export interface ProfileDirectionRow {
  direction: string
  label: string
  successfulChallengeCount: number
}

export function buildProfileDirectionRows(
  directions: ReadonlyArray<{ direction?: string | null, successfulChallengeCount?: number | null }>,
): ProfileDirectionRow[] {
  const counts = new Map<string, number>()
  for (const item of directions) {
    const key = directionKey(item.direction)
    if (!key) continue
    counts.set(key, (counts.get(key) ?? 0) + (item.successfulChallengeCount ?? 0))
  }
  const catalogOrder = new Map(challengeDirectionOptions.map((direction, index) =>
    [directionKey(direction), index]))
  return [...counts].map(([direction, successfulChallengeCount]) => ({
    direction,
    label: directionLabel(direction),
    successfulChallengeCount,
  })).sort((left, right) =>
    (catalogOrder.get(left.direction) ?? Number.MAX_SAFE_INTEGER)
      - (catalogOrder.get(right.direction) ?? Number.MAX_SAFE_INTEGER)
    || left.label.localeCompare(right.label))
}
