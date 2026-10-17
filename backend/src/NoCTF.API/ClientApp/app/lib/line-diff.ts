export interface LineDiffRow {
  /** Line from the original document, or null when this row only adds a line. */
  before: string | null
  /** Line from the updated document, or null when this row only removes a line. */
  after: string | null
  changed: boolean
}

/** Above this many cells the middle block is paired by index instead of aligned. */
const maximumAlignmentCells = 160_000

function alignChangedBlock(before: string[], after: string[]): LineDiffRow[] {
  if (!before.length) return after.map(line => ({ before: null, after: line, changed: true }))
  if (!after.length) return before.map(line => ({ before: line, after: null, changed: true }))
  if (before.length * after.length > maximumAlignmentCells) {
    const rows: LineDiffRow[] = []
    for (let index = 0; index < Math.max(before.length, after.length); index++)
      rows.push({ before: before[index] ?? null, after: after[index] ?? null, changed: true })
    return rows
  }

  const table: number[][] = Array.from({ length: before.length + 1 }, () => new Array<number>(after.length + 1).fill(0))
  for (let row = before.length - 1; row >= 0; row--) {
    for (let column = after.length - 1; column >= 0; column--) {
      table[row]![column] = before[row] === after[column]
        ? table[row + 1]![column + 1]! + 1
        : Math.max(table[row + 1]![column]!, table[row]![column + 1]!)
    }
  }

  const rows: LineDiffRow[] = []
  let row = 0
  let column = 0
  while (row < before.length && column < after.length) {
    if (before[row] === after[column]) {
      rows.push({ before: before[row]!, after: after[column]!, changed: false })
      row++
      column++
    }
    else if (table[row + 1]![column]! >= table[row]![column + 1]!) {
      rows.push({ before: before[row]!, after: null, changed: true })
      row++
    }
    else {
      rows.push({ before: null, after: after[column]!, changed: true })
      column++
    }
  }
  while (row < before.length) rows.push({ before: before[row++]!, after: null, changed: true })
  while (column < after.length) rows.push({ before: null, after: after[column++]!, changed: true })
  return rows
}

/**
 * Line comparison for review surfaces. The common prefix and suffix are matched
 * first and only the changed middle is aligned, so an insertion no longer marks
 * every following line as changed.
 */
export function diffLines(beforeText: string, afterText: string): LineDiffRow[] {
  const before = beforeText.split('\n')
  const after = afterText.split('\n')

  let start = 0
  while (start < before.length && start < after.length && before[start] === after[start]) start++
  let endBefore = before.length - 1
  let endAfter = after.length - 1
  while (endBefore >= start && endAfter >= start && before[endBefore] === after[endAfter]) {
    endBefore--
    endAfter--
  }

  const rows: LineDiffRow[] = []
  for (let index = 0; index < start; index++)
    rows.push({ before: before[index]!, after: after[index]!, changed: false })
  rows.push(...alignChangedBlock(before.slice(start, endBefore + 1), after.slice(start, endAfter + 1)))
  for (let index = endBefore + 1; index < before.length; index++) {
    const afterIndex = endAfter + 1 + (index - (endBefore + 1))
    rows.push({ before: before[index]!, after: after[afterIndex]!, changed: false })
  }
  return rows
}
