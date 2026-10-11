import { describe, expect, test } from 'bun:test'
import { columnOffsets, virtualColumnRange, virtualEntries, virtualRange } from '../app/components/ui/table/virtual-table-window'

describe('two-axis table window', () => {
  test('a 1000 by 1000 matrix mounts only the viewport and buffer at both ends and in the middle', () => {
    const items = Array.from({ length: 1000 }, (_, i) => i)
    const offsets = columnOffsets(items.map(index => ({ key: String(index), width: 224 })))
    for (const index of [0, 500, 990]) {
      const rows = virtualEntries(items, virtualRange(1000, 88, index * 88, 880), i => i * 88)
      const cols = virtualEntries(items, virtualColumnRange(offsets, index * 224, 1200), i => offsets[i]!)
      expect(rows.length).toBeLessThanOrEqual(16)
      expect(cols.length).toBeLessThanOrEqual(8)
      expect(rows.some(row => row.index === index)).toBeTrue()
      expect(cols.some(col => col.index === index)).toBeTrue()
      expect(rows.reduce((height, row) => height + row.before + 88, 0) + (999 - rows.at(-1)!.index) * 88).toBe(88000)
    }
  })
  test('retains a keyboard-focused cell without mounting the entire gap', () => {
    const items = Array.from({ length: 1000 }, (_, i) => i)
    const rows = virtualEntries(items, { start: 500, end: 510 }, i => i * 88, 2)
    expect(rows.length).toBe(11)
    expect(rows[0]!.index).toBe(2)
    expect(rows[1]!.before).toBe(497 * 88)
  })
  test('handles partial multi-round groups, empty results and viewport above/below the table', () => {
    const offsets = columnOffsets([{ key: 'a', width: 112 }, { key: 'b', width: 224 }, { key: 'c', width: 112 }])
    expect(virtualColumnRange(offsets, 112, 112)).toEqual({ start: 0, end: 3 })
    expect(virtualRange(0, 88, 1000, 880)).toEqual({ start: 0, end: 0 })
    expect(virtualRange(100, 88, -1500, 880).end).toBe(3)
    expect(virtualRange(100, 88, 100000, 880)).toEqual({ start: 100, end: 100 })
    expect(virtualEntries([], { start: 0, end: 0 }, i => i * 88)).toEqual([])
  })
})
