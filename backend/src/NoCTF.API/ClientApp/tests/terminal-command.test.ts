import { expect, test } from 'bun:test'
import { terminalEnterAction } from '../app/components/ui/terminal/terminal-keys'

const enter = { key: 'Enter', shiftKey: false, isComposing: false, repeat: false, keyCode: 13 }
const ready = { composing: false, multiple: false, blocked: false, value: 'flag{test}' }

test('terminal submits Enter but never IME confirmation or repeated Enter', () => {
  expect(terminalEnterAction(enter, ready)).toBe('submit')
  expect(terminalEnterAction({ ...enter, isComposing: true }, ready)).toBe('ignore')
  expect(terminalEnterAction({ ...enter, keyCode: 229 }, ready)).toBe('ignore')
  expect(terminalEnterAction(enter, { ...ready, composing: true })).toBe('ignore')
  expect(terminalEnterAction({ ...enter, repeat: true }, ready)).toBe('prevent')
})

test('terminal prevents empty and blocked submissions and supports batch newlines', () => {
  expect(terminalEnterAction(enter, { ...ready, value: '  \n ' })).toBe('prevent')
  expect(terminalEnterAction(enter, { ...ready, blocked: true })).toBe('prevent')
  expect(terminalEnterAction({ ...enter, shiftKey: true }, { ...ready, multiple: true })).toBe('newline')
  expect(terminalEnterAction(enter, { ...ready, multiple: true, value: 'flag{a}\nflag{b}' })).toBe('submit')
  expect(terminalEnterAction({ ...enter, key: 'ArrowLeft' }, ready)).toBe('ignore')
})

test('terminal exposes the themed flag placeholder without covering it with the synthetic caret', async () => {
  const terminal = await Bun.file(new URL('../app/components/ui/terminal/TerminalCommand.vue', import.meta.url)).text()
  const css = await Bun.file(new URL('../app/assets/css/main.css', import.meta.url)).text()

  expect(terminal).toContain(':placeholder="placeholder"')
  expect(terminal).toContain('!blocked && !placeholder')
  expect(css).toContain("[data-slot='terminal-editor']::placeholder")
  expect(css).toContain("font-family: 'Microsoft YaHei'")
  expect(css).toContain('font-style: italic;')
})
