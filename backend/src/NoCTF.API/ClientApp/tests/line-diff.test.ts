import { describe, expect, test } from 'bun:test'
import { diffLines } from '../app/lib/line-diff'

describe('line diff for review surfaces', () => {
  test('an insertion only marks the inserted line', () => {
    const rows = diffLines('a\nb\nc', 'a\nnew\nb\nc')
    expect(rows.filter(row => row.changed)).toEqual([{ before: null, after: 'new', changed: true }])
    expect(rows.filter(row => !row.changed).map(row => row.before)).toEqual(['a', 'b', 'c'])
  })

  test('a deletion only marks the removed line', () => {
    const rows = diffLines('a\nb\nc', 'a\nc')
    expect(rows.filter(row => row.changed)).toEqual([{ before: 'b', after: null, changed: true }])
  })

  test('a replacement reports both sides of the changed pair', () => {
    const rows = diffLines('a\nx', 'a\ny')
    expect(rows.filter(row => row.changed)).toEqual([
      { before: 'x', after: null, changed: true },
      { before: null, after: 'y', changed: true },
    ])
  })

  test('identical documents and empty documents stay unchanged', () => {
    expect(diffLines('a\nb', 'a\nb').every(row => !row.changed)).toBeTrue()
    expect(diffLines('', '')).toEqual([{ before: '', after: '', changed: false }])
    expect(diffLines('', 'new').map(row => row.after).filter(Boolean)).toEqual(['new'])
    expect(diffLines('old', '').map(row => row.before).filter(Boolean)).toEqual(['old'])
  })

  test('large changed blocks fall back to index pairing without losing lines', () => {
    const before = Array.from({ length: 500 }, (_, index) => 'old-' + index).join('\n')
    const after = Array.from({ length: 500 }, (_, index) => 'new-' + index).join('\n')
    const rows = diffLines(before, after)
    expect(rows).toHaveLength(500)
    expect(rows.every(row => row.changed)).toBeTrue()
  })
})
