import { describe, expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'
import { restoreProgressionViewport } from '../app/components/ui/progression/progression-viewport'
import type { ProgressionViewport } from '../app/components/ui/progression/progression-viewport'
import {
  applyProgressionSelectionChanges,
  canConnectProgression,
  getProgressionSelection,
  removeProgressionSelection,
  updateProgressionNodePositions,
} from '../app/lib/progression-graph'

describe('progression editor connections', () => {
  test('allows a new edge and independent badge paths', () => {
    expect(canConnectProgression([{ source: 'challenge', target: 'badge-a' }],
      'challenge', 'badge-b')).toBe(true)
  })

  test('rejects a self edge, duplicate edge and a transitive cycle', () => {
    const edges = [
      { source: 'a', target: 'b' },
      { source: 'b', target: 'c' },
    ]
    expect(canConnectProgression(edges, 'a', 'a')).toBe(false)
    expect(canConnectProgression(edges, 'a', 'b')).toBe(false)
    expect(canConnectProgression(edges, 'c', 'a')).toBe(false)
    expect(canConnectProgression(edges, 'c', 'd')).toBe(true)
  })
})

describe('progression editor selection', () => {
  test('the canvas uses Ctrl multi-select and forwards Vue Flow selection and drag events', () => {
    const canvas = readFileSync(new URL('../app/components/ui/progression/ProgressionCanvas.vue', import.meta.url), 'utf8')
    expect(canvas).toContain(":multi-selection-key-code=\"props.readOnly ? null : 'Control'\"")
    expect(canvas).toContain('@nodes-change="onNodesChange"')
    expect(canvas).toContain('@edges-change="onEdgesChange"')
    expect(canvas).toContain('@node-drag-stop="onNodeDragStop"')
  })

  test('a single selection keeps the individual inspector available', () => {
    const selection = getProgressionSelection(
      [{ id: 'a' }, { id: 'b' }],
      [{ id: 'edge', source: 'a', target: 'b' }],
      new Set(['a']), new Set(),
    )
    expect(selection.count).toBe(1)
    expect(selection.node?.id).toBe('a')
    expect(selection.edge).toBeNull()
  })

  test('multiple selected nodes and edges do not show a single-item inspector', () => {
    const selection = getProgressionSelection(
      [{ id: 'a' }, { id: 'b' }],
      [{ id: 'edge', source: 'a', target: 'b' }],
      new Set(['a', 'b']), new Set(['edge']),
    )
    expect(selection.count).toBe(3)
    expect(selection.node).toBeNull()
    expect(selection.edge).toBeNull()
  })

  test('removing a multi-selection also removes connections touching removed nodes', () => {
    const remaining = removeProgressionSelection(
      [{ id: 'a' }, { id: 'b' }, { id: 'c' }],
      [
        { id: 'ab', source: 'a', target: 'b' },
        { id: 'bc', source: 'b', target: 'c' },
        { id: 'cb', source: 'c', target: 'b' },
      ],
      new Set(['a']), new Set(['bc']),
    )
    expect(remaining.nodes.map(node => node.id)).toEqual(['b', 'c'])
    expect(remaining.edges.map(edge => edge.id)).toEqual(['cb'])
  })

  test('Ctrl-click toggles selection without dropping other selected elements', () => {
    const first = applyProgressionSelectionChanges(new Set<string>(), [
      { id: 'a', selected: true },
    ])
    const second = applyProgressionSelectionChanges(first, [
      { id: 'b', selected: true },
    ])
    expect([...second]).toEqual(['a', 'b'])
    expect([...applyProgressionSelectionChanges(second, [
      { id: 'a', selected: false },
    ])]).toEqual(['b'])
  })

  test('a group drag stores every selected node position in the save draft', () => {
    const nodes = [
      { id: 'a', position: { x: 0, y: 0 } },
      { id: 'b', position: { x: 20, y: 10 } },
      { id: 'c', position: { x: 40, y: 30 } },
    ]
    const result = updateProgressionNodePositions(nodes, [
      { id: 'a', x: 100, y: 50 },
      { id: 'b', x: 120, y: 60 },
    ])
    expect(result.map(node => node.position)).toEqual([
      { x: 100, y: 50 }, { x: 120, y: 60 }, { x: 40, y: 30 },
    ])
  })
})

describe('progression canvas viewport', () => {
  test('the canvas exposes a fit-all action and permits zooming out after nodes are imported', () => {
    const canvas = readFileSync(new URL('../app/components/ui/progression/ProgressionCanvas.vue', import.meta.url), 'utf8')
    expect(canvas).toContain('@init="onInit"')
    expect(canvas).toContain(':min-zoom="0.001"')
    expect(canvas).toContain('@click="restoreView"')
    expect(canvas).toContain("$t('progression.fitView')")
  })

  test('finds all nodes without changing their saved coordinates', async () => {
    const calls: unknown[] = []
    const viewport: ProgressionViewport = {
      fitView: async options => { calls.push(['fit', options]); return true },
      setViewport: async transform => { calls.push(['set', transform]); return true },
    }
    await restoreProgressionViewport(viewport, true)
    expect(calls).toEqual([['fit', { padding: 0.2, minZoom: 0.001, maxZoom: 1 }]])
  })

  test('returns an empty canvas to its default view', async () => {
    const calls: unknown[] = []
    const viewport: ProgressionViewport = {
      fitView: async options => { calls.push(['fit', options]); return true },
      setViewport: async transform => { calls.push(['set', transform]); return true },
    }
    await restoreProgressionViewport(viewport, false)
    expect(calls).toEqual([['set', { x: 0, y: 0, zoom: 1 }]])
  })
})

describe('progression badge image upload', () => {
  test('create and edit expose the shared visible uploader instead of an unreachable file input', () => {
    const view = readFileSync(new URL('../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdProgressionPageView.vue', import.meta.url), 'utf8')
    const upload = readFileSync(new URL('../app/components/ui/upload/FileUpload.vue', import.meta.url), 'utf8')
    expect(view.match(/<FileUpload /g)).toHaveLength(2)
    expect(view).not.toContain('<FileInput')
    expect(view).toContain(':key="newBadgeUploadKey"')
    expect(view).toContain(':key="editBadgeUploadKey"')
    expect(view).toContain(':disabled="badgeSaving || !newBadgeName.trim() || !newBadgeImage"')
    expect(upload).toContain('@click="choose"')
    expect(upload).toContain('@drop.prevent="drop"')
  })

  test('successful creation and switching the edited badge clear the previous file selection', () => {
    const feature = readFileSync(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdProgressionPage.ts', import.meta.url), 'utf8')
    expect(feature).toContain('newBadgeUploadKey.value++')
    expect(feature).toContain('editBadgeUploadKey.value++')
    expect(feature).toContain('newBadgeImage.value = null')
    expect(feature).toContain('editBadgeImage.value = null')
  })
})
