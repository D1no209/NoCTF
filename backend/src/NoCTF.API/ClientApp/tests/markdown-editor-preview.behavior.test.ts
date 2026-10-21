import { afterAll, afterEach, beforeAll, expect, test } from 'bun:test'
import { EditorState } from '@codemirror/state'
import { EditorView } from '@codemirror/view'
import { Window } from 'happy-dom'
import { livePreview } from '../app/components/ui/markdown-editor/live-preview'

const browser = new Window({ url: 'http://localhost/' })
const globals = new Map<string, PropertyDescriptor | undefined>()
const editors: EditorView[] = []

beforeAll(() => {
  const bindings: Record<string, unknown> = { window: browser, Worker: undefined }
  for (const key of ['document', 'navigator', 'Node', 'Element', 'HTMLElement', 'Text', 'MutationObserver', 'DOMRect', 'Range'])
    bindings[key] = Reflect.get(browser, key)
  bindings.requestAnimationFrame = browser.requestAnimationFrame.bind(browser)
  bindings.cancelAnimationFrame = browser.cancelAnimationFrame.bind(browser)
  bindings.getComputedStyle = browser.getComputedStyle.bind(browser)
  for (const [key, value] of Object.entries(bindings)) {
    globals.set(key, Object.getOwnPropertyDescriptor(globalThis, key))
    Object.defineProperty(globalThis, key, { value, configurable: true, writable: true })
  }
})

afterEach(() => {
  for (const editor of editors.splice(0)) editor.destroy()
  document.body.replaceChildren()
})

afterAll(async () => {
  await browser.happyDOM.abort()
  for (const [key, descriptor] of globals) {
    if (descriptor) Object.defineProperty(globalThis, key, descriptor)
    else Reflect.deleteProperty(globalThis, key)
  }
})

function editor(doc: string, focused = true) {
  const view = new EditorView({ parent: document.body, state: EditorState.create({ doc, extensions: [livePreview] }) })
  editors.push(view)
  if (focused) view.focus()
  return view
}

async function settle() {
  await new Promise(resolve => setTimeout(resolve, 30))
}

test('unfocused mixed documents preview their first HTML block and Markdown together', async () => {
  const source = '<div style="font-weight:700;text-align:right">出题人：PaperPlane</div>\n\n## Description\n\nRead **carefully** and use `libpaper.so`.\n\n- Download the attachment'
  const view = editor(source, false)
  await settle()
  const author = view.dom.querySelector<HTMLDivElement>('.cm-md-block > div')
  expect(author?.textContent).toBe('出题人：PaperPlane')
  expect(author?.style.textAlign).toBe('right')
  expect(view.dom.querySelector('.cm-md-strong')?.textContent).toBe('carefully')
  expect(view.dom.querySelector('.cm-md-code')?.textContent).toBe('libpaper.so')
  expect(view.state.doc.toString()).toBe(source)
})

test('focus and blur restore mixed previews without moving the caret or changing the document', async () => {
  const source = '<div style="text-align:right" onclick="bad()">Author<script>bad()</script></div>\n\nRead **carefully** with `libpaper.so`.'
  const view = editor(source, false)
  await settle()
  view.focus()
  await settle()
  expect(view.hasFocus).toBe(true)
  expect(view.dom.querySelector('.cm-md-block')).toBeNull()
  expect(view.dom.querySelector('.cm-md-strong')?.textContent).toBe('carefully')
  view.contentDOM.blur()
  await settle()
  expect(view.hasFocus).toBe(false)
  expect(view.dom.querySelector('.cm-md-block > div')?.textContent).toBe('Author')
  expect(view.dom.querySelector('.cm-md-strong')?.textContent).toBe('carefully')
  expect(view.dom.querySelector('script, [onclick]')).toBeNull()

  const caret = source.indexOf('carefully')
  view.dispatch({ selection: { anchor: caret } })
  expect(view.dom.querySelector('.cm-md-strong')?.textContent).toBe('carefully')
  view.focus()
  await settle()
  expect(view.dom.querySelector('.cm-md-strong')).toBeNull()
  expect(view.dom.querySelector('.cm-md-block > div')?.textContent).toBe('Author')
  view.contentDOM.blur()
  await settle()
  expect(view.dom.querySelector('.cm-md-strong')?.textContent).toBe('carefully')
  expect(view.dom.querySelector('.cm-md-code')?.textContent).toBe('libpaper.so')
  expect(view.state.selection.main.head).toBe(caret)
  expect(view.state.doc.toString()).toBe(source)
})

test('fenced blocks mount, reveal their source at the caret and survive text updates', async () => {
  const source = 'Intro\n\n```js\nconsole.log(1)\n```\nEnd'
  const view = editor(source)
  await settle()
  expect(view.dom.querySelector('.cm-md-block code')?.textContent).toBe('console.log(1)')
  view.dispatch({ selection: { anchor: view.state.doc.line(4).from } })
  expect(view.dom.querySelector('.cm-md-block')).toBeNull()
  view.dispatch({ selection: { anchor: 0 }, changes: { from: 0, insert: 'New ' } })
  expect(view.dom.querySelector('.cm-md-block code')?.textContent).toBe('console.log(1)')
  expect(view.state.doc.toString()).toBe('New ' + source)
})

test('table blocks finish rendering and keep working when the caret enters and leaves', async () => {
  const view = editor('Intro\n\n| A | B |\n| --- | --- |\n| 1 | 2 |\nEnd')
  await settle()
  expect(view.dom.querySelectorAll('.cm-md-block td').length).toBe(2)
  view.dispatch({ selection: { anchor: view.state.doc.line(3).from } })
  expect(view.dom.querySelector('table')).toBeNull()
  view.dispatch({ selection: { anchor: 0 } })
  expect(view.dom.querySelectorAll('.cm-md-block td').length).toBe(2)
})

test('HTML author blocks share sanitized alignment and reveal editable source at the caret', async () => {
  const source = 'Intro\n\n<div style="text-align: right" onclick="bad()">出题人：PaperPlane</div>\n\nEnd'
  const view = editor(source)
  await settle()
  let author = view.dom.querySelector<HTMLDivElement>('.cm-md-block > div')
  expect(author?.textContent).toBe('出题人：PaperPlane')
  expect(author?.style.textAlign).toBe('right')
  expect(author?.hasAttribute('onclick')).toBe(false)
  view.dispatch({ selection: { anchor: view.state.doc.line(3).from } })
  expect(view.dom.querySelector('.cm-md-block')).toBeNull()
  expect(view.state.doc.toString()).toBe(source)
  view.dispatch({ selection: { anchor: 0 } })
  author = view.dom.querySelector<HTMLDivElement>('.cm-md-block > div')
  expect(author?.style.textAlign).toBe('right')
})

test('HTML preview removes executable content and keeps fenced HTML literal', async () => {
  const view = editor('Intro\n\n<div><script>bad()</script><iframe srcdoc="bad"></iframe><strong>safe</strong></div>\n\n```html\n<div onclick="bad()">literal</div>\n```')
  await settle()
  expect(view.dom.querySelector('.cm-md-block strong')?.textContent).toBe('safe')
  expect(view.dom.querySelector('script, iframe, [onclick]')).toBeNull()
  expect(view.dom.querySelector('.cm-md-block code')?.textContent).toContain('<div onclick="bad()">literal</div>')
})

test('composition reveals source until its final text has been committed', async () => {
  const view = editor('Intro\n\n```text\nbody\n```')
  view.contentDOM.dispatchEvent(new browser.CompositionEvent('compositionstart', { bubbles: true }))
  expect(view.dom.querySelector('.cm-md-block')).toBeNull()
  view.dispatch({ changes: { from: 0, insert: '中' } })
  view.contentDOM.dispatchEvent(new browser.CompositionEvent('compositionend', { bubbles: true }))
  await settle()
  expect(view.dom.querySelector('.cm-md-block code')?.textContent).toBe('body')
  expect(view.state.doc.toString().startsWith('中Intro')).toBe(true)
})

test('destroying an editor cancels deferred composition updates', async () => {
  const view = editor('Intro\n\n```text\nbody\n```')
  view.contentDOM.dispatchEvent(new browser.CompositionEvent('compositionend', { bubbles: true }))
  view.destroy()
  editors.splice(editors.indexOf(view), 1)
  await settle()
  expect(view.dom.isConnected).toBe(false)
})
