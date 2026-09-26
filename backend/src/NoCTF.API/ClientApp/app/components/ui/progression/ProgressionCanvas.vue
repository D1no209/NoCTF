<script setup lang="ts">
import { Handle, Panel, Position, VueFlow } from '@vue-flow/core'
import type { Connection, Edge, EdgeChange, Node, NodeChange, NodeDragEvent, VueFlowStore } from '@vue-flow/core'
import { Scan } from '@lucide/vue'
import { nextTick, shallowRef } from 'vue'
import { restoreProgressionViewport } from './progression-viewport'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'

const props = withDefaults(defineProps<{
  readOnly?: boolean
  height?: string
}>(), { readOnly: false, height: '38rem' })
const nodes = defineModel<Node[]>('nodes', { required: true })
const edges = defineModel<Edge[]>('edges', { required: true })
const fitViewOnInit = nodes.value.length > 0
const viewport = shallowRef<Pick<VueFlowStore, 'fitView' | 'setViewport'> | null>(null)
const emit = defineEmits<{
  connect: [connection: Connection]
  nodeSelectionChange: [changes: { id: string, selected: boolean }[]]
  edgeSelectionChange: [changes: { id: string, selected: boolean }[]]
  nodePositionsChange: [positions: { id: string, x: number, y: number }[]]
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
}

async function restoreView() {
  if (!viewport.value) return
  await nextTick()
  await restoreProgressionViewport(viewport.value, nodes.value.length > 0)
}
</script>

<template>
  <VueFlow v-model:nodes="nodes" v-model:edges="edges"
    :fit-view-on-init="fitViewOnInit"
    :default-viewport="{ x: 0, y: 0, zoom: 1 }"
    :min-zoom="0.001"
    :nodes-draggable="!props.readOnly" :nodes-connectable="!props.readOnly"
    :elements-selectable="!props.readOnly" :multi-selection-key-code="props.readOnly ? null : 'Control'"
    :edges-updatable="!props.readOnly" :delete-key-code="props.readOnly ? null : ['Backspace', 'Delete']"
    :style="{ height: props.height }"
    @init="onInit" @connect="emit('connect', $event)"
    @nodes-change="onNodesChange" @edges-change="onEdgesChange"
    @node-drag-stop="onNodeDragStop" @selection-drag-stop="onNodeDragStop">
    <Panel position="top-right">
      <Hint :content="$t('progression.fitView')">
        <Button type="button" variant="secondary" size="icon" class="size-11" :disabled="!viewport"
          :aria-label="$t('progression.fitView')" @click="restoreView">
          <Scan data-icon="inline-start" />
        </Button>
      </Hint>
    </Panel>
    <template #node-progression="{ data }">
      <div class="progression-node-card flex min-w-44 max-w-56 items-center gap-2 rounded-lg border border-border bg-background px-3 py-2 shadow-sm">
        <Handle type="target" :position="Position.Left" :connectable="!props.readOnly" />
        <img v-if="data.imageUrl" :src="data.imageUrl" alt="" class="size-9 rounded object-cover" />
        <span class="min-w-0 break-words text-sm font-medium">{{ data.title }}</span>
        <Handle type="source" :position="Position.Right" :connectable="!props.readOnly" />
      </div>
    </template>
    <template #node-read-progression="{ data }">
      <div class="min-w-36 max-w-52 rounded-lg border px-3 py-2 text-sm shadow-sm"
        :class="data.active ? 'border-primary bg-card' : 'border-border bg-muted text-muted-foreground'">
        <Handle type="target" :position="Position.Left" :connectable="false" />
        <img v-if="data.imageUrl" :src="data.imageUrl" alt="" class="mb-1 size-9 rounded object-cover" />
        <p class="font-medium">{{ data.title }}</p>
        <p class="mt-1 text-xs">{{ data.complete ? $t('progression.completed') : data.active ? $t('progression.available') : $t('progression.locked') }}</p>
        <Handle type="source" :position="Position.Right" :connectable="false" />
      </div>
    </template>
  </VueFlow>
</template>
