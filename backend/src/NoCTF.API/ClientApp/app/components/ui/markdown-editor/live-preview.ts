import { RangeSetBuilder } from '@codemirror/state'
import { Decoration, EditorView, ViewPlugin, WidgetType, type DecorationSet, type ViewUpdate } from '@codemirror/view'

/**
 * Obsidian 式实时预览:编辑器内容仍是 Markdown 源码,非光标行隐藏语法标记并套用排版样式,
 * 光标所在行恢复原始标记以便编辑。
 */

class BulletWidget extends WidgetType {
  override toDOM() {
    const span = document.createElement('span')
    span.className = 'cm-md-bullet'
    span.textContent = '\u2022'
    return span
  }
}

const bullet = Decoration.replace({ widget: new BulletWidget() })
const hide = Decoration.replace({})
const strong = Decoration.mark({ class: 'cm-md-strong' })
const emphasis = Decoration.mark({ class: 'cm-md-em' })
const inlineCode = Decoration.mark({ class: 'cm-md-code' })
const linkText = Decoration.mark({ class: 'cm-md-link' })
const quoteLine = Decoration.line({ class: 'cm-md-quote' })
const fenceLine = Decoration.line({ class: 'cm-md-fence' })
const codeLine = Decoration.line({ class: 'cm-md-codeline' })
const headings = [1, 2, 3, 4, 5, 6].map(level => Decoration.line({ class: `cm-md-h${level}` }))

type Range = { from: number, to: number, deco: Decoration }

function scan(view: EditorView): DecorationSet {
  const { state } = view
  const cursorLines = new Set<number>()
  for (const range of state.selection.ranges) {
    cursorLines.add(state.doc.lineAt(range.head).number)
    cursorLines.add(state.doc.lineAt(range.from).number)
    cursorLines.add(state.doc.lineAt(range.to).number)
  }
  const ranges: Range[] = []
  let fence: string | null = null
  for (let number = 1; number <= state.doc.lines; number += 1) {
    const line = state.doc.line(number)
    const text = line.text
    const trimmed = text.trimStart()
    const at = line.from
    if (trimmed.startsWith('```') || trimmed.startsWith('~~~')) {
      const marker = trimmed.startsWith('```') ? '```' : '~~~'
      if (fence === null) {
        fence = marker
        ranges.push({ from: at, to: at, deco: fenceLine })
      }
      else if (fence === marker) {
        fence = null
        ranges.push({ from: at, to: at, deco: fenceLine })
      }
      continue
    }
    if (fence !== null) { ranges.push({ from: at, to: at, deco: codeLine }); continue }
    if (!text.trim()) continue
    // 光标行保留源码,不做隐藏
    const editable = cursorLines.has(number)
    const heading = /^(#{1,6})\s+/.exec(text)
    if (heading) {
      ranges.push({ from: at, to: at, deco: headings[heading[1]!.length - 1]! })
      if (!editable) ranges.push({ from: at, to: at + heading[0].length, deco: hide })
      continue
    }
    const quote = /^>\s?/.exec(text)
    if (quote) {
      ranges.push({ from: at, to: at, deco: quoteLine })
      if (!editable) ranges.push({ from: at, to: at + quote[0].length, deco: hide })
      continue
    }
    const list = /^(\s+)?([-*+])\s/.exec(text)
    if (list) {
      const start = at + (list[1]?.length ?? 0)
      if (!editable) ranges.push({ from: start, to: start + 1, deco: bullet })
      continue
    }
    if (editable) continue
    const used: Array<{ from: number, to: number }> = []
    const overlap = (from: number, to: number) => used.some(item => from < item.to && to > item.from)
    const push = (from: number, to: number, deco: Decoration) => { used.push({ from, to }); ranges.push({ from, to, deco }) }
    // 加粗、行内代码、链接先处理,再处理斜体,避免区间重叠
    for (const match of text.matchAll(/\*\*([^*\n]+)\*\*/g)) {
      const start = at + match.index!
      const inner = start + 2
      const innerEnd = inner + match[1]!.length
      if (overlap(start, start + match[0].length)) continue
      push(start, inner, hide); push(inner, innerEnd, strong); push(innerEnd, start + match[0].length, hide)
    }
    for (const match of text.matchAll(/`([^`\n]+)`/g)) {
      const start = at + match.index!
      const inner = start + 1
      const innerEnd = inner + match[1]!.length
      if (overlap(start, start + match[0].length)) continue
      push(start, inner, hide); push(inner, innerEnd, inlineCode); push(innerEnd, start + match[0].length, hide)
    }
    for (const match of text.matchAll(/\[([^\]\n]+)\]\(([^)\n]+)\)/g)) {
      const start = at + match.index!
      const textEnd = start + 1 + match[1]!.length
      if (overlap(start, start + match[0].length)) continue
      push(start, start + 1, hide); push(start + 1, textEnd, linkText); push(textEnd, start + match[0].length, hide)
    }
    for (const match of text.matchAll(/(^|[^*])\*([^*\n]+)\*(?!\*)/g)) {
      const prefix = match[1]!.length
      const start = at + match.index! + prefix
      const inner = start + 1
      const innerEnd = inner + match[2]!.length
      if (overlap(start, start + match[0].length - prefix)) continue
      push(start, inner, hide); push(inner, innerEnd, emphasis); push(innerEnd, start + match[0].length - prefix, hide)
    }
  }
  ranges.sort((left, right) => left.from - right.from || left.to - right.to)
  const builder = new RangeSetBuilder<Decoration>()
  for (const range of ranges) builder.add(range.from, range.to, range.deco)
  return builder.finish()
}

export const livePreview = ViewPlugin.fromClass(class {
  decorations: DecorationSet
  constructor(view: EditorView) { this.decorations = scan(view) }
  update(update: ViewUpdate) {
    if (update.docChanged || update.selectionSet || update.viewportChanged) this.decorations = scan(update.view)
  }
}, { decorations: value => value.decorations })
