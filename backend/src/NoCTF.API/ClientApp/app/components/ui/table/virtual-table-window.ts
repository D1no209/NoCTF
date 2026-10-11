export interface VirtualColumn { key: string; width: number; groupKey?: string }

export function virtualRange(count: number, size: number, offset: number, viewport: number, overscan = 3) {
  const start = Math.max(0, Math.min(count, Math.floor(Math.max(0, offset) / size) - overscan))
  const end = Math.max(start, Math.min(count, Math.ceil(Math.max(0, offset + viewport) / size) + overscan))
  return { start, end }
}

export function columnOffsets(columns: readonly VirtualColumn[]): number[] {
  const offsets = [0]
  for (const column of columns) offsets.push(offsets.at(-1)! + column.width)
  return offsets
}

export function virtualColumnRange(offsets: readonly number[], left: number, width: number) {
  const count = offsets.length - 1
  function boundary(value: number) {
    let low = 0
    let high = count
    while (low < high) {
      const mid = (low + high) >>> 1
      if (offsets[mid + 1]! <= value) low = mid + 1
      else high = mid
    }
    return low
  }
  return { start: Math.max(0, boundary(left) - 1), end: Math.min(count, boundary(left + width) + 2) }
}

/** Keep a focused item mounted without expanding the window across thousands of items. */
export function virtualEntries<T>(items: readonly T[], range: { start: number; end: number }, offset: (index: number) => number, focused = -1) {
  const indices = Array.from({ length: range.end - range.start }, (_, index) => range.start + index)
  if (focused >= 0 && focused < items.length && !indices.includes(focused)) indices.push(focused)
  indices.sort((a, b) => a - b)
  let previous = 0
  return indices.map(index => {
    const before = offset(index) - offset(previous)
    previous = index + 1
    return { item: items[index]!, index, before }
  })
}
