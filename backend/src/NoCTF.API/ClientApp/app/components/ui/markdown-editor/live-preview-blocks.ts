import type { EditorState } from '@codemirror/state'

/** 编辑器实时预览识别的整块:代码围栏或表格。 */
export interface PreviewBlock {
  fromLine: number
  toLine: number
  source: string[]
  /** 表格块的原始源码(用于 worker 渲染与缓存键)。 */
  tableSource?: string
}

/** 拆表格行:去掉首尾管道后按未转义的 | 分列。 */
export function tableCells(line: string): string[] {
  const trimmed = line.trim().replace(/^\|/, '').replace(/\|$/, '')
  return trimmed.split(/(?<!\\)\|/)
}

/** 表格分隔行必须与表头列数一致,且每列都是 :?-{2,}:? 。 */
export function isTableDelimiter(line: string, columns: number): boolean {
  const cells = tableCells(line)
  return cells.length === columns && cells.every(cell => /^:?-{2,}:?$/.test(cell.trim()))
}

/**
 * 找出文档中的代码围栏与表格整块。
 *
 * 表格只包含 Markdown 实际会渲染出的行:只有管道的空行与超出表头列数的行
 * 会被渲染器丢弃或渲染成空白,继续吞进整块里等于隐藏用户输入的字符。
 */
export function collectBlocks(state: EditorState): PreviewBlock[] {
  const text = (number: number) => state.doc.line(number).text
  const blocks: PreviewBlock[] = []
  for (let number = 1; number <= state.doc.lines; number += 1) {
    const line = text(number).trimStart()
    const fence = /^(```|~~~)/.exec(line)
    if (fence) {
      const marker = fence[1]!
      let end = number
      while (end < state.doc.lines) {
        end += 1
        if (text(end).trimStart().startsWith(marker) && end > number) break
      }
      if (end > number) {
        const source: string[] = []
        for (let index = number; index <= end; index += 1) source.push(text(index))
        blocks.push({ fromLine: number, toLine: end, source })
        number = end
      }
      continue
    }
    if (text(number).includes('|') && number + 1 <= state.doc.lines) {
      const columns = tableCells(text(number)).length
      if (!isTableDelimiter(text(number + 1), columns)) continue
      let end = number + 1
      while (end < state.doc.lines) {
        const next = text(end + 1)
        if (!next.includes('|') || !next.trim()) break
        const cells = tableCells(next)
        // 多余列会被渲染器丢弃,只有管道的行会渲染成不可见行:两种情况都保留原文。
        if (cells.length > columns || cells.every(cell => cell.trim() === '')) break
        end += 1
      }
      const source: string[] = []
      for (let index = number; index <= end; index += 1) source.push(text(index))
      blocks.push({ fromLine: number, toLine: end, source, tableSource: source.join('\n') })
      number = end
    }
  }
  return blocks
}

/** 光标(或选区)覆盖到的行:这些行必须保持源码可编辑。 */
export function selectedLines(state: EditorState): Set<number> {
  const lines = new Set<number>()
  for (const range of state.selection.ranges) {
    lines.add(state.doc.lineAt(range.head).number)
    lines.add(state.doc.lineAt(range.from).number)
    lines.add(state.doc.lineAt(range.to).number)
  }
  return lines
}

/** 光标不在其中的整块才会被替换成渲染结果。 */
export function renderedBlockList(state: EditorState): PreviewBlock[] {
  const lines = selectedLines(state)
  return collectBlocks(state).filter((block) => {
    for (let line = block.fromLine; line <= block.toLine; line += 1) if (lines.has(line)) return false
    return true
  })
}

/** 行装饰需要跳过的整块区间:fromLine -> toLine。 */
export function renderedBlockLines(state: EditorState): Map<number, number> {
  const map = new Map<number, number>()
  for (const block of renderedBlockList(state)) map.set(block.fromLine, block.toLine)
  return map
}
