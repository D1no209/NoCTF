<script setup lang="ts">
import { Handle, Panel, Position, VueFlow } from '@vue-flow/core'
import type { Connection, Edge, EdgeChange, Node, NodeChange, NodeDragEvent, VueFlowStore } from '@vue-flow/core'
import { LockKeyhole, LocateFixed, Scan } from '@lucide/vue'
import { computed, nextTick, shallowRef, watch } from 'vue'
import { progressionFocusTransform, restoreProgressionViewport } from './progression-viewport'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'

const props = withDefaults(defineProps<{
  readOnly?: boolean
  height?: string
  batchMode?: boolean
  batchSourceId?: string | null
  batchSelectedIds?: ReadonlySet<string>
  batchDisabledReasons?: Record<string, string>
  previewEdges?: Edge[]
  focusNodeId?: string | null
  currentProgressNodeId?: string | null
  highlightedNodeIds?: ReadonlySet<string>
  highlightedEdgeIds?: ReadonlySet<string>
  showProgressControls?: boolean
  direction?: 'RIGHT' | 'DOWN'
  layoutRevision?: number
}>(), { readOnly: false, height: '38rem' })
const nodes = defineModel<Node[]>('nodes', { required: true })
const edges = defineModel<Edge[]>('edges', { required: true })
const viewport = shallowRef<Pick<VueFlowStore, 'fitView' | 'setViewport' | 'setCenter' | 'dimensions'> | null>(null)
const displayEdges = computed(() => [...edges.value.map(edge => ({
  ...edge,
  class: [edge.class, props.highlightedEdgeIds?.has(edge.id) ? 'progression-blocked-edge' : '']
    .filter(Boolean).join(' '),
})), ...(props.previewEdges ?? [])])
const emit = defineEmits<{
  connect: [connection: Connection]
  nodeSelectionChange: [changes: { id: string, selected: boolean }[]]
  edgeSelectionChange: [changes: { id: string, selected: boolean }[]]
  nodePositionsChange: [positions: { id: string, x: number, y: number }[]]
  nodeClick: [id: string]
}>()

function onNodesChange(changes: NodeChange[]) {
  const selection = changes.flatMap(change => change.type === 'select'
    ? [{ id: change.id, selected: change.selected }]
    : change.type === 'remove' ? [{ id: change.id, selected: false }] : [])
  if (selection.length) emit('nodeSelectionChange', selection)
}

function onEdgesChange(changes: EdgeChange[]) {
  const selection = changes.flatMap(change => change.type === 'select'
    ? [{ id: change.id, selected: change.selected }]
    : change.type === 'remove' ? [{ id: change.id, selected: false }] : [])
  if (selection.length) emit('edgeSelectionChange', selection)
}

function onNodeDragStop(event: NodeDragEvent) {
  emit('nodePositionsChange', event.nodes.map(node => ({
    id: node.id, x: node.position.x, y: node.position.y,
  })))
}

function onInit(instance: VueFlowStore) {
  viewport.value = instance
  if (props.focusNodeId) void focusNode(props.focusNodeId)
}

watch(() => props.focusNodeId, id => { if (id) void focusNode(id) })
watch(() => props.layoutRevision, revision => {
  if (revision && !props.showProgressControls) void restoreView()
})
watch(() => props.highlightedNodeIds, ids => {
  if (!ids?.size || !viewport.value) return
  void viewport.value.fitView({ nodes: [...ids], padding: 0.4, minZoom: 0.65, maxZoom: 1 })
})

async function focusNode(id: string) {
  const node = nodes.value.find(item => item.id === id)
  if (!node || !viewport.value) return
  await nextTick()
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  const dimensions = viewport.value.dimensions.value
  const nodeWidth = props.readOnly ? 240 : 200
  const nodeHeight = props.readOnly ? 96 : 76
  if (dimensions.width && dimensions.height) {
    await viewport.value.setViewport(progressionFocusTransform({
      x: node.position.x, y: node.position.y,
      width: nodeWidth, height: nodeHeight,
    }, dimensions, props.direction ?? 'RIGHT'), { duration: reduced ? 0 : 180 })
  }
  else {
    await viewport.value.setCenter(node.position.x + nodeWidth / 2,
      node.position.y + nodeHeight / 2,
      { zoom: 1, duration: reduced ? 0 : 180 })
  }
}

async function restoreView() {
  if (!viewport.value) return
  await nextTick()
  await restoreProgressionViewport(viewport.value, nodes.value.length > 0)
}
</script>

<template>
  <VueFlow v-model:nodes="nodes" :edges="displayEdges"
    :fit-view-on-init="!props.showProgressControls && nodes.length > 0"
    :default-viewport="{ x: 0, y: 0, zoom: 1 }"
    :min-zoom="0.001"
    :nodes-draggable="!props.readOnly && !props.batchMode" :nodes-connectable="!props.readOnly && !props.batchMode"
    :elements-selectable="!props.readOnly && !props.batchMode" :multi-selection-key-code="props.readOnly || props.batchMode ? null : 'Control'"
    :edges-updatable="!props.readOnly && !props.batchMode" :delete-key-code="props.readOnly || props.batchMode ? null : ['Backspace', 'Delete']"
    :style="{ height: props.height }"
    @init="onInit" @connect="emit('connect', $event)"
    @node-click="emit('nodeClick', $event.node.id)"
    @nodes-change="onNodesChange" @edges-change="onEdgesChange"
    @node-drag-stop="onNodeDragStop" @selection-drag-stop="onNodeDragStop">
    <Panel position="top-right" class="flex gap-2">
      <Hint v-if="props.showProgressControls" :content="$t('progression.returnToProgress')">
        <Button type="button" variant="secondary" size="icon" class="size-11"
          :disabled="!viewport || !props.currentProgressNodeId" :aria-label="$t('progression.returnToProgress')"
          @click="props.currentProgressNodeId && focusNode(props.currentProgressNodeId)">
          <LocateFixed data-icon="inline-start" />
        </Button>
      </Hint>
      <Hint :content="$t('progression.fitView')">
        <Button type="button" variant="secondary" size="icon" class="size-11" :disabled="!viewport"
          :aria-label="$t('progression.fitView')" @click="restoreView">
          <Scan data-icon="inline-start" />
        </Button>
      </Hint>
    </Panel>
    <template #node-progression="{ id, data }">
      <Hint :content="props.batchDisabledReasons?.[id] || data.title">
        <div class="progression-node-card flex w-[200px] min-h-[76px] items-center gap-2 rounded-lg bg-background px-3 py-2 shadow-sm"
          :class="props.batchSelectedIds?.has(id) ? 'bg-primary/15 shadow-md' : props.batchSourceId === id ? 'bg-primary/10' : props.batchDisabledReasons?.[id] ? 'opacity-55' : ''"
          :aria-disabled="props.batchDisabledReasons?.[id] ? true : undefined" tabindex="0"
          @keydown.enter.prevent="emit('nodeClick', id)">
          <Handle type="target" :position="props.direction === 'DOWN' ? Position.Top : Position.Left" :connectable="!props.readOnly && !props.batchMode" />
        <img v-if="data.imageUrl" :src="data.imageUrl" alt="" class="size-9 rounded object-cover" />
          <span class="min-w-0 line-clamp-2 break-words text-sm font-medium">{{ data.title }}</span>
          <Handle type="source" :position="props.direction === 'DOWN' ? Position.Bottom : Position.Right" :connectable="!props.readOnly && !props.batchMode" />
        </div>
      </Hint>
    </template>
    <template #node-read-progression="{ id, data }">
      <Hint :content="data.title">
        <div class="flex w-[240px] min-h-[96px] cursor-pointer items-center gap-3 rounded-lg px-4 py-3 text-sm shadow-sm"
          :class="[data.complete ? 'bg-success/15 text-success' : !data.active ? 'bg-muted text-muted-foreground' : data.visited ? 'bg-warning/15 text-warning' : 'bg-card text-primary', props.highlightedNodeIds?.has(id) ? 'progression-blocked-node' : '']"
          tabindex="0" @keydown.enter.prevent="emit('nodeClick', id)">
          <Handle type="target" :position="props.direction === 'DOWN' ? Position.Top : Position.Left" :connectable="false" />
          <img v-if="data.imageUrl" :src="data.imageUrl" alt="" class="size-9 rounded object-cover" />
          <div class="min-w-0 flex-1">
            <p class="line-clamp-2 break-words text-base font-semibold">{{ data.title }}</p>
            <p class="mt-1 flex items-center gap-1 text-sm">
              <LockKeyhole v-if="!data.active" class="size-3" aria-hidden="true" />
              {{ data.complete ? $t('progression.completed') : data.active ? data.visited ? $t('progression.inProgress') : $t('progression.available') : $t('progression.locked') }}
              <span v-if="data.complete && !data.active">· {{ $t('progression.locked') }}</span>
            </p>
          </div>
          <Handle type="source" :position="props.direction === 'DOWN' ? Position.Bottom : Position.Right" :connectable="false" />
        </div>
      </Hint>
    </template>
  </VueFlow>
</template>
