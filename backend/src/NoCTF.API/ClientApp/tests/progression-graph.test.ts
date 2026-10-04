import { describe, expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'
import { progressionFocusTransform, restoreProgressionViewport } from '../app/components/ui/progression/progression-viewport'
import { buildProgressionLayout, progressionTopologyKey } from '../app/lib/progression-layout'
import type { ElkNode } from 'elkjs/lib/elk-api.js'
import type { ProgressionViewport } from '../app/components/ui/progression/progression-viewport'
import {
  applyProgressionSelectionChanges,
  canConnectProgression,
  chooseProgressionFocus,
  maximumProgressionEdges,
  progressionBatchIssue,
  progressionConnectionIssue,
  unsatisfiedProgressionEdges,
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
    expect(progressionConnectionIssue(edges, 'a', 'a')).toBe('self')
    expect(progressionConnectionIssue(edges, 'a', 'b')).toBe('duplicate')
    expect(progressionConnectionIssue(edges, 'c', 'a')).toBe('cycle')
  })

  test('a batch may add independent successors but not provisional duplicate targets', () => {
    const edges = [{ source: 'source', target: 'first' }]
    expect(progressionConnectionIssue(edges, 'source', 'second')).toBeNull()
    expect(progressionConnectionIssue([...edges, { source: 'source', target: 'second' }],
      'source', 'second')).toBe('duplicate')
    expect(progressionBatchIssue(edges, 'source', ['second', 'third'])).toBeNull()
    expect(progressionBatchIssue(edges, 'source', ['second', 'second'])).toBe('duplicate')
    expect(progressionBatchIssue(edges, 'source', ['second', 'first'])).toBe('duplicate')
  })

  test('disables additional targets at the server edge limit', () => {
    const edges = Array.from({ length: maximumProgressionEdges }, (_, index) => ({
      source: `s${index}`, target: `t${index}`,
    }))
    expect(progressionConnectionIssue(edges, 'new-source', 'new-target')).toBe('limit')
  })
})

describe('per-node prerequisite policy', () => {
  test('the inspector edits the selected node and the save request carries its policy', () => {
    const view = readFileSync(new URL('../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdProgressionPageView.vue', import.meta.url), 'utf8')
    const feature = readFileSync(new URL('../app/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdProgressionPage.ts', import.meta.url), 'utf8')
    expect(view).toContain("$t('progression.challengeRequiresPrerequisites')")
    expect(view).toContain("$t('progression.badgeRequiresPrerequisites')")
    expect(view).toContain('@update:model-value="setSelectedNodeRequiresPrerequisites($event)"')
    expect(feature).toContain('requiresPrerequisites: node.data!.requiresPrerequisites')
    expect(feature).toContain('requiresPrerequisites: true')
  })

  test('player map shows why a node with unmet edges is already active', () => {
    const view = readFileSync(new URL('../app/components/views/page/competitions/[id]/CompetitionsByIdProgressionPageView.vue', import.meta.url), 'utf8')
    expect(view).toContain("$t('progression.challengeAlwaysOpen')")
    expect(view).toContain("$t('progression.badgeAlwaysActive')")
  })
})

describe('progression editor selection', () => {
  test('the canvas uses Ctrl multi-select and forwards Vue Flow selection and drag events', () => {
    const canvas = readFileSync(new URL('../app/components/ui/progression/ProgressionCanvas.vue', import.meta.url), 'utf8')
    expect(canvas).toContain(":multi-selection-key-code=\"props.readOnly || props.batchMode ? null : 'Control'\"")
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

  test('a group drag updates only the temporary editor positions', () => {
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

  test('places the current node ahead of center so successors remain visible', () => {
    const node = { x: 500, y: 200, width: 240, height: 96 }
    const viewport = { width: 1200, height: 800 }
    expect(progressionFocusTransform(node, viewport, 'RIGHT')).toEqual({
      x: -260, y: 152, zoom: 1,
    })
    expect(progressionFocusTransform(node, viewport, 'DOWN')).toEqual({
      x: -20, y: -8, zoom: 1,
    })
  })
})

describe('progression automatic layout', () => {
  const graphNodes = [
    { id: 'first', kind: 0 as const },
    { id: 'second', kind: 0 as const },
    { id: 'third', kind: 1 as const },
  ]
  const graphEdges = [{ source: 'first', target: 'second' }]

  test('the same topology yields the same positions regardless of input ordering', async () => {
    const deterministic = async (graph: ElkNode) => ({
      ...graph, children: graph.children?.map((node, index) => ({
        ...node, x: index * 300, y: 0,
      })),
    })
    const first = await buildProgressionLayout(graphNodes, graphEdges, 'RIGHT', deterministic)
    const second = await buildProgressionLayout([...graphNodes].reverse(), graphEdges, 'RIGHT', deterministic)
    expect(first).toEqual(second)
    expect(first.find(node => node.id === 'first')!.x)
      .toBeLessThan(first.find(node => node.id === 'second')!.x)
  })

  test('narrow layouts put successors below predecessors', async () => {
    let input: ElkNode | null = null
    await buildProgressionLayout(graphNodes, graphEdges, 'DOWN', async graph => {
      input = graph
      return graph
    })
    expect(input!.layoutOptions?.['elk.direction']).toBe('DOWN')
    expect(input!.layoutOptions?.['elk.layered.spacing.nodeNodeBetweenLayers']).toBe('110')
  })

  test('player nodes can request larger dimensions without changing saved graph data', async () => {
    let input: ElkNode | null = null
    await buildProgressionLayout([{ id: 'player', kind: 0, width: 240, height: 96 }], [],
      'RIGHT', async graph => { input = graph; return graph })
    expect(input!.children?.[0]?.width).toBe(240)
    expect(input!.children?.[0]?.height).toBe(96)
  })

  test('completion and condition changes do not change the topology key', () => {
    expect(progressionTopologyKey(graphNodes, graphEdges, 'RIGHT'))
      .toBe(progressionTopologyKey([...graphNodes].reverse(), graphEdges, 'RIGHT'))
    expect(progressionTopologyKey(graphNodes, graphEdges, 'RIGHT'))
      .not.toBe(progressionTopologyKey(graphNodes, graphEdges, 'DOWN'))
  })
})

describe('player progression workspace', () => {
  test('the map fills its route and moves to the viewport without remounting', () => {
    const view = readFileSync(new URL('../app/components/views/page/competitions/[id]/CompetitionsByIdProgressionPageView.vue', import.meta.url), 'utf8')
    const parent = readFileSync(new URL('../app/components/views/page/competitions/CompetitionsByIdPageView.vue', import.meta.url), 'utf8')
    expect(view).toContain('<Teleport to="body" :disabled="!isExpanded">')
    expect(view).toContain('height="100%"')
    expect(view).not.toContain('height="35rem"')
    expect(parent).toContain(':data-progression-route="isProgression"')
  })

  test('badges and node details open on demand instead of preceding the canvas', () => {
    const view = readFileSync(new URL('../app/components/views/page/competitions/[id]/CompetitionsByIdProgressionPageView.vue', import.meta.url), 'utf8')
    expect(view).toContain('<Sheet v-model:open="badgeSheetOpen">')
    expect(view).toContain('data-progression-inspector')
    expect(view).toContain('@click="enterSelectedChallenge"')
  })

  test('badge rows show only image and name until their details are opened', () => {
    const view = readFileSync(new URL('../app/components/views/page/competitions/[id]/CompetitionsByIdProgressionPageView.vue', import.meta.url), 'utf8')
    const feature = readFileSync(new URL('../app/features/routes/competitions/[id]/useCompetitionsByIdProgressionPage.ts', import.meta.url), 'utf8')
    const badgeRow = view.split('v-for="badge in data.badges"')[1]!.split('v-show="selectedBadgeId === badge.id"')[0]!
    expect(badgeRow).toContain(':src="badge.imageUrl ?? undefined"')
    expect(badgeRow).toContain('{{ badge.name }}')
    expect(badgeRow).not.toContain('badge.description')
    expect(view).toContain(':aria-expanded="selectedBadgeId === badge.id"')
    expect(feature).toContain('selectedBadgeId.value = selectedBadgeId.value === badgeId ? null : badgeId')
    expect(feature).toMatch(/selectedBadgeId\.value = null\r?\n    badgeSheetOpen\.value = true/)
  })
})

describe('player progression state', () => {
  test('focuses the most recently started available challenge without depending on completion color', () => {
    const base = { kind: 0 as const, active: true, complete: false, visited: true, x: 0, y: 0 }
    expect(chooseProgressionFocus([
      { ...base, id: 'older', firstOpenedAt: '2026-09-01T00:00:00Z' },
      { ...base, id: 'newer', firstOpenedAt: '2026-09-02T00:00:00Z' },
      { ...base, id: 'locked', active: false, firstOpenedAt: '2026-09-03T00:00:00Z' },
    ])).toBe('newer')
  })

  test('both completed and incomplete edge conditions report only unmet direct predecessors', () => {
    const edges = [
      { id: 'completed', source: 'a', target: 'destination', condition: 0 as const },
      { id: 'incomplete', source: 'b', target: 'destination', condition: 1 as const },
      { id: 'unrelated', source: 'a', target: 'other', condition: 0 as const },
    ]
    expect(unsatisfiedProgressionEdges(new Map([['a', false], ['b', true]]),
      edges, 'destination').map(edge => edge.id)).toEqual(['completed', 'incomplete'])
    expect(unsatisfiedProgressionEdges(new Map([['a', true], ['b', false]]),
      edges, 'destination')).toEqual([])
  })
})

describe('progression badge image upload', () => {
  test('create and edit expose the shared visible uploader instead of an unreachable file input', () => {
    const view = readFileSync(new URL('../app/components/views/page/admin/competitions/[id]/AdminCompetitionsByIdProgressionPageView.vue', import.meta.url), 'utf8')
    const upload = readFileSync(new URL('../app/components/ui/upload/FileUpload.vue', import.meta.url), 'utf8')
    expect(view.match(/<FileUpload /g)).toHaveLength(2)
    expect(view).not.toContain('<FileInput')
    expect(view).toContain(':key="newBadgeUploadKey ?? undefined"')
    expect(view).toContain(':key="editBadgeUploadKey ?? undefined"')
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
