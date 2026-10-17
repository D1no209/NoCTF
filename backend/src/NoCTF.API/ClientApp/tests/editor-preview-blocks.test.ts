import { describe, expect, test } from 'bun:test'
import { EditorState, EditorSelection } from '@codemirror/state'
import { collectBlocks, renderedBlockList, tableCells } from '../app/components/ui/markdown-editor/live-preview-blocks'

function blocksOf(doc: string) {
  return collectBlocks(EditorState.create({ doc }))
}

describe('editor live preview block detection', () => {
  test('a well formed table becomes one rendered block', () => {
    const blocks = blocksOf('| a | b |\n| --- | --- |\n| 1 | 2 |\n| 3 | 4 |')
    expect(blocks).toHaveLength(1)
    expect(blocks[0]!.fromLine).toBe(1)
    expect(blocks[0]!.toLine).toBe(4)
    expect(blocks[0]!.tableSource).toContain('| 3 | 4 |')
  })

  test('a pipe-only row stays outside the block so the typed characters stay visible', () => {
    const blocks = blocksOf('| a | b |\n| --- | --- |\n|||')
    expect(blocks).toHaveLength(1)
    expect(blocks[0]!.toLine).toBe(2)
  })

  test('a row with more cells than the header stays outside the block', () => {
    const blocks = blocksOf('| a | b |\n| --- | --- |\n| 1 | 2 | 3 |')
    expect(blocks).toHaveLength(1)
    expect(blocks[0]!.toLine).toBe(2)
  })

  test('a pseudo table the renderer would reject is not a block at all', () => {
    expect(blocksOf('|||\n| --- |')).toHaveLength(0)
    expect(blocksOf('| a | b |\n| --- |')).toHaveLength(0)
  })

  test('fences still form blocks and table cells ignore escaped pipes', () => {
    const blocks = blocksOf('```js\nconst a = 1\n```')
    expect(blocks).toHaveLength(1)
    expect(blocks[0]!.fromLine).toBe(1)
    expect(blocks[0]!.toLine).toBe(3)
    expect(tableCells('| a \\| b | c |')).toEqual([' a \\| b ', ' c '])
  })

  test('a block containing the cursor is not rendered', () => {
    const doc = '| a | b |\n| --- | --- |'
    const state = EditorState.create({ doc, selection: EditorSelection.cursor(3) })
    expect(collectBlocks(state)).toHaveLength(1)
    expect(renderedBlockList(state)).toHaveLength(0)
  })
})
