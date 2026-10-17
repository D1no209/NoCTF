import { RangeSetBuilder, StateEffect, StateField, type EditorState } from '@codemirror/state'
import { Decoration, EditorView, ViewPlugin, WidgetType, type DecorationSet } from '@codemirror/view'
import { renderMarkdownPreview } from '../../../lib/markdown-preview-worker.client'
import { renderMarkdownFence } from '../../../lib/markdown-fence'
import { highlightMarkdownCode } from '../../../lib/markdown-highlighter'
import { renderedBlockList, selectedLines, type PreviewBlock } from './live-preview-blocks'

/**
 * Obsidian 式实时预览:编辑器内容仍是 Markdown 源码,非光标行隐藏语法标记并套用排版样式,
 * 光标所在行恢复原始标记以便编辑。代码围栏与表格整块渲染为与只读展示相同的 HTML。
 */

class BulletWidget extends WidgetType {
  override toDOM() {
    const span = document.createElement('span')
    span.className = 'cm-md-bullet'
    span.textContent = '\u2022'
    return span
  }
}

/** 表格渲染缓存:同一份源码只走一次 worker,并限制条目数避免长会话无界增长。 */
const renderedBlocks = new Map<string, string>()
const renderedBlocksLimit = 32

class BlockWidget extends WidgetType {
  constructor(private readonly html: string, private readonly source?: string) { super() }
  override eq(other: BlockWidget) {
    // 表格按源码复用同一个 widget:worker 返回后不再触发一次替换/重排。
    if (this.source !== undefined || other.source !== undefined) return other.source === this.source
    return other.html === this.html
  }
  override toDOM(view: EditorView) {
    const wrapper = document.createElement('div')
    wrapper.className = 'cm-md-block markdown-content'
    if (this.html) { wrapper.innerHTML = this.html; return wrapper }
    const source = this.source ?? ''
    const cached = renderedBlocks.get(source)
    if (cached !== undefined) {
      if (cached.includes('<table')) wrapper.innerHTML = cached
      else wrapper.textContent = source
      return wrapper
    }
    void renderMarkdownPreview(source).then((html) => {
      renderedBlocks.set(source, html)
      if (renderedBlocks.size > renderedBlocksLimit)
        renderedBlocks.delete(renderedBlocks.keys().next().value!)
      if (!wrapper.isConnected) return
      // 渲染器没有产出表格时保留原文,绝不把用户内容变成空白块。
      if (html.includes('<table')) wrapper.innerHTML = html
      else wrapper.textContent = source
      view.requestMeasure()
    })
    return wrapper
  }
  override ignoreEvent() { return false }
}

/**
 * 围栏 HTML 与发布渲染共用同一实现:语言 token 会被规范化,作者输入的围栏
 * info 无法闭合属性或注入元素。
 */
function codeBlockHtml(source: string[]): string {
  const open = source[0] ?? ''
  const info = open.replace(/^```|^~~~/, '').trim()
  const body = source.slice(1, source.length - 1).join('\n')
  return renderMarkdownFence(body, info, highlightMarkdownCode)
}

const bullet = Decoration.replace({ widget: new BulletWidget() })
const hide = Decoration.replace({})
const strong = Decoration.mark({ class: 'cm-md-strong' })
const emphasis = Decoration.mark({ class: 'cm-md-em' })
const inlineCode = Decoration.mark({ class: 'cm-md-code' })
const linkText = Decoration.mark({ class: 'cm-md-link' })
const quoteLine = Decoration.line({ class: 'cm-md-quote' })
const listLine = Decoration.line({ class: 'cm-md-list' })
const headings = [1, 2, 3, 4, 5, 6].map(level => Decoration.line({ class: `cm-md-h${level}` }))

type Range = { from: number, to: number, deco: Decoration }

function scan(state: EditorState, composing: boolean): DecorationSet {
  const cursorLines = selectedLines(state)
  // 输入法组合期间保持源码可见:替换型装饰会打断候选词并让光标跳位。
  const rendered = new Map<number, PreviewBlock>()
  if (!composing) for (const block of renderedBlockList(state)) rendered.set(block.fromLine, block)
  const ranges: Range[] = []
  for (let number = 1; number <= state.doc.lines; number += 1) {
    const line = state.doc.line(number)
    const body = line.text
    const block = rendered.get(number)
    if (block) {
      const cached = block.tableSource === undefined ? undefined : renderedBlocks.get(block.tableSource)
      // 已知渲染结果不是表格(原文被丢弃)时保留源码,不显示空块。
      const skip = block.tableSource !== undefined && cached !== undefined && !cached.includes('<table')
      if (!skip) {
        const html = block.tableSource === undefined ? codeBlockHtml(block.source) : cached ?? ''
        const end = state.doc.line(block.toLine)
        ranges.push({ from: line.from, to: end.to, deco: Decoration.replace({ block: true, widget: new BlockWidget(html, block.tableSource) }) })
        number = block.toLine
        continue
      }
    }
    const trimmed = body.trimStart()
    if (!trimmed) continue
    const at = line.from
    const editable = composing || cursorLines.has(number)
    const heading = /^(#{1,6})\s+/.exec(body)
    if (heading) {
      ranges.push({ from: at, to: at, deco: headings[heading[1]!.length - 1]! })
      if (!editable) ranges.push({ from: at, to: at + heading[0].length, deco: hide })
    }
    const quote = /^>\s?/.exec(body)
    if (quote) {
      ranges.push({ from: at, to: at, deco: quoteLine })
      if (!editable) ranges.push({ from: at, to: at + quote[0].length, deco: hide })
    }
    const list = /^(\s+)?([-*+])\s/.exec(body)
    if (list) {
      const start = at + (list[1]?.length ?? 0)
      ranges.push({ from: at, to: at, deco: listLine })
      if (!editable) ranges.push({ from: start, to: start + 1, deco: bullet })
    }
    if (editable) continue
    const used: Array<{ from: number, to: number }> = []
    const overlap = (from: number, to: number) => used.some(item => from < item.to && to > item.from)
    const push = (from: number, to: number, deco: Decoration) => { used.push({ from, to }); ranges.push({ from, to, deco }) }
    for (const match of body.matchAll(/\*\*([^*\n]+)\*\*/g)) {
      const start = at + match.index!
      const inner = start + 2
      const innerEnd = inner + match[1]!.length
      if (overlap(start, start + match[0].length)) continue
      push(start, inner, hide); push(inner, innerEnd, strong); push(innerEnd, start + match[0].length, hide)
    }
    for (const match of body.matchAll(/`([^`\n]+)`/g)) {
      const start = at + match.index!
      const inner = start + 1
      const innerEnd = inner + match[1]!.length
      if (overlap(start, start + match[0].length)) continue
      push(start, inner, hide); push(inner, innerEnd, inlineCode); push(innerEnd, start + match[0].length, hide)
    }
    for (const match of body.matchAll(/\[([^\]\n]+)\]\(([^)\n]+)\)/g)) {
      const start = at + match.index!
      const textEnd = start + 1 + match[1]!.length
      if (overlap(start, start + match[0].length)) continue
      push(start, start + 1, hide); push(start + 1, textEnd, linkText); push(textEnd, start + match[0].length, hide)
    }
    for (const match of body.matchAll(/(^|[^*])\*([^*\n]+)\*(?!\*)/g)) {
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

const compositionChanged = StateEffect.define<boolean>()
const previewDecorations = StateField.define<{ composing: boolean; decorations: DecorationSet }>({
  create: state => ({ composing: false, decorations: scan(state, false) }),
  update(value, transaction) {
    let composing = value.composing
    for (const effect of transaction.effects)
      if (effect.is(compositionChanged)) composing = effect.value
    if (!transaction.docChanged && !transaction.selection && composing === value.composing) return value
    return { composing, decorations: scan(transaction.state, composing) }
  },
  // 多行替换会改变布局,必须在计算视口之前直接提供,不能从 ViewPlugin 返回。
  provide: field => EditorView.decorations.from(field, value => value.decorations),
})

const compositionEvents = ViewPlugin.fromClass(class {
  private timer: ReturnType<typeof setTimeout> | undefined
  constructor(readonly view: EditorView) {}
  start() {
    clearTimeout(this.timer)
    this.view.dispatch({ effects: compositionChanged.of(true) })
  }
  finish() {
    clearTimeout(this.timer)
    // 等待 CodeMirror 提交组合输入的最后一轮文本后再恢复预览。
    this.timer = setTimeout(() => {
      this.view.dispatch({ effects: compositionChanged.of(false) })
    }, 0)
  }
  destroy() { clearTimeout(this.timer) }
}, {
  eventHandlers: {
    compositionstart() { this.start() },
    compositionend() { this.finish() },
    blur() { this.finish() },
  },
})

export const livePreview = [previewDecorations, compositionEvents]
