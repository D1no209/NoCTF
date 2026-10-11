/** Step-end lines retain the previous score until the next event. */
export function scoreTrendValueAt(points: readonly (readonly [number, number])[], at: number): number {
  let low = 0
  let high = points.length
  while (low < high) {
    const middle = Math.floor((low + high) / 2)
    if (points[middle]![0] <= at) low = middle + 1
    else high = middle
  }
  return low === 0 ? 0 : points[low - 1]![1]
}
