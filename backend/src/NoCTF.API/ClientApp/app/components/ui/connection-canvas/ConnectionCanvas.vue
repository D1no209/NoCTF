<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { themeColor, themeColorAlpha } from '~/utils/theme-color'
import { layoutConnections } from './connection-layout'
import type { ConnectionEdge, ConnectionNode } from './connection-layout'

const props = defineProps<{
  nodes: ConnectionNode[]
  edges: ConnectionEdge[]
  selectedId?: string
  label: string
  sourceLabel: string
  targetLabel: string
}>()
const emit = defineEmits<{ select: [id: string] }>()
const root = ref<HTMLElement | null>(null)
const canvas = ref<HTMLCanvasElement | null>(null)
const availableWidth = ref(480)
const layout = computed(() => layoutConnections(props.nodes, availableWidth.value))
let resizeObserver: ResizeObserver | undefined
let themeObserver: MutationObserver | undefined
let frame = 0
let disposed = false

function render() {
  const element = canvas.value
  const context = element?.getContext('2d')
  if (!element || !context) return
  const { width, height, nodes } = layout.value
  const ratio = Math.min(window.devicePixelRatio || 1, 2)
  element.width = Math.round(width * ratio)
  element.height = Math.round(height * ratio)
  context.setTransform(ratio, 0, 0, ratio, 0, 0)
  context.clearRect(0, 0, width, height)
  context.fillStyle = themeColorAlpha('--muted-foreground', 0.15, element)
  for (let x = 8; x < width; x += 20) {
    for (let y = 8; y < height; y += 20) {
      context.beginPath()
      context.arc(x, y, 0.7, 0, Math.PI * 2)
      context.fill()
    }
  }
  const byId = new Map(nodes.map(node => [node.id, node]))
  for (const edge of props.edges) {
    const source = byId.get(edge.source)
    const target = byId.get(edge.target)
    if (!source || !target) continue
    const start = { x: source.x + source.width, y: source.y + source.height / 2 }
    const end = { x: target.x, y: target.y + target.height / 2 }
    const selected = source.id === props.selectedId || target.id === props.selectedId
    const color = source.invalid || target.invalid ? '--destructive' : selected ? '--primary' : '--muted-foreground'
    context.strokeStyle = themeColorAlpha(color, selected ? 0.8 : 0.4, element)
    context.lineWidth = selected ? 2 : 1
    context.beginPath()
    context.moveTo(start.x, start.y)
    const midpoint = (start.x + end.x) / 2
    context.bezierCurveTo(midpoint, start.y, midpoint, end.y, end.x, end.y)
    context.stroke()
    context.fillStyle = themeColor(color, element)
    for (const point of [start, end]) {
      context.beginPath()
      context.arc(point.x, point.y, selected ? 3 : 2, 0, Math.PI * 2)
      context.fill()
    }
  }
}

function scheduleRender() {
  if (disposed) return
  cancelAnimationFrame(frame)
  frame = requestAnimationFrame(() => { frame = 0; render() })
}

async function revealSelection() {
  await nextTick()
  const viewport = root.value?.querySelector<HTMLElement>('[data-slot="scroll-surface"]')
  const selected = layout.value.nodes.find(node => node.id === props.selectedId)
  if (!viewport || !selected) return
  if (selected.y < viewport.scrollTop) viewport.scrollTop = selected.y
  else if (selected.y + selected.height > viewport.scrollTop + viewport.clientHeight)
    viewport.scrollTop = selected.y + selected.height - viewport.clientHeight
}

onMounted(() => {
  const viewport = root.value?.querySelector<HTMLElement>('[data-slot="scroll-surface"]')
  resizeObserver = new ResizeObserver(entries => {
    availableWidth.value = Math.floor(entries[0]?.contentRect.width ?? 480)
    scheduleRender()
  })
  if (viewport) resizeObserver.observe(viewport)
  themeObserver = new MutationObserver(scheduleRender)
  themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['class', 'style'] })
  scheduleRender()
})
watch(() => [props.nodes, props.edges, props.selectedId], scheduleRender, { deep: true })
watch(() => props.selectedId, revealSelection)
onBeforeUnmount(() => {
  disposed = true
  resizeObserver?.disconnect()
  themeObserver?.disconnect()
  cancelAnimationFrame(frame)
})
</script>

<template>
  <section ref="root" data-slot="connection-canvas" :aria-label="label" class="min-w-0">
    <div class="grid grid-cols-2 gap-6 px-4 pb-2 text-xs font-medium text-muted-foreground">
      <span>{{ sourceLabel }}</span>
      <span>{{ targetLabel }}</span>
    </div>
    <ScrollSurface axis="both" class="max-h-64 overflow-auto rounded-md bg-muted/25">
      <div class="relative" :style="{ width: `${layout.width}px`, height: `${layout.height}px` }">
        <canvas ref="canvas" aria-hidden="true" class="pointer-events-none absolute inset-0 size-full" />
        <template v-for="node in layout.nodes" :key="node.id">
          <Hint :content="[node.label, node.detail].filter(Boolean).join(' · ')">
            <Button
              v-if="node.selectable"
              type="button"
              variant="ghost"
              :aria-pressed="selectedId === node.id"
              :aria-label="[node.label, node.detail].filter(Boolean).join(' · ')"
              class="absolute flex h-auto min-w-0 flex-col items-start justify-center gap-1 px-3 text-left"
              :class="node.invalid ? 'bg-destructive/10 text-destructive' : selectedId === node.id ? 'bg-primary/10 text-primary' : 'bg-control/80 text-foreground'"
              :style="{ left: `${node.x}px`, top: `${node.y}px`, width: `${node.width}px`, height: `${node.height}px` }"
              @click="emit('select', node.id)"
            >
              <span class="w-full truncate font-mono text-sm font-semibold">{{ node.label }}</span>
              <span v-if="node.detail" class="w-full truncate text-xs font-normal text-muted-foreground">{{ node.detail }}</span>
            </Button>
            <div
              v-else
              tabindex="0"
              class="absolute flex min-w-0 flex-col justify-center gap-1 rounded-md px-3"
              :class="node.invalid ? 'bg-destructive/10 text-destructive' : 'bg-control/80 text-foreground'"
              :style="{ left: `${node.x}px`, top: `${node.y}px`, width: `${node.width}px`, height: `${node.height}px` }"
            >
              <span class="truncate font-mono text-sm font-semibold">{{ node.label }}</span>
              <span v-if="node.detail" class="truncate text-xs text-muted-foreground">{{ node.detail }}</span>
            </div>
          </Hint>
        </template>
      </div>
    </ScrollSurface>
  </section>
</template>
