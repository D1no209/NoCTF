<script setup lang="ts">
import { Handle, Position, VueFlow } from '@vue-flow/core'
import type { Connection, Edge, Node } from '@vue-flow/core'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'

const props = withDefaults(defineProps<{
  readOnly?: boolean
  height?: string
}>(), { readOnly: false, height: '38rem' })
const nodes = defineModel<Node[]>('nodes', { required: true })
const edges = defineModel<Edge[]>('edges', { required: true })
const fitViewOnInit = nodes.value.length > 0
const emit = defineEmits<{
  connect: [connection: Connection]
  nodeSelect: [nodeId: string]
  edgeSelect: [edgeId: string]
}>()
</script>

<template>
  <VueFlow v-model:nodes="nodes" v-model:edges="edges"
    :fit-view-on-init="fitViewOnInit"
    :default-viewport="{ x: 0, y: 0, zoom: 1 }"
    :min-zoom="fitViewOnInit || props.readOnly ? 0.25 : 1"
    :nodes-draggable="!props.readOnly" :nodes-connectable="!props.readOnly"
    :edges-updatable="!props.readOnly" :delete-key-code="props.readOnly ? null : ['Backspace', 'Delete']"
    :style="{ height: props.height }"
    @connect="emit('connect', $event)"
    @node-click="emit('nodeSelect', $event.node.id)"
    @edge-click="emit('edgeSelect', $event.edge.id)">
    <template #node-progression="{ data }">
      <div class="flex min-w-44 max-w-56 items-center gap-2 rounded-lg border border-border bg-background px-3 py-2 shadow-sm">
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
