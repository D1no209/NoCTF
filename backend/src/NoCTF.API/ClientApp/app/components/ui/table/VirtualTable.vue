<script setup lang="ts" generic="Row, Column extends VirtualColumn">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { columnOffsets, virtualColumnRange, virtualEntries, virtualRange } from './virtual-table-window'
import type { VirtualColumn } from './virtual-table-window'

const props = withDefaults(defineProps<{
  rows: Row[]
  columns: Column[]
  rowHeight?: number
  fixedWidths: number[]
}>(), { rowHeight: 88 })
const root = ref<HTMLElement | null>(null)
const table = ref<HTMLTableElement | null>(null)
const rowRange = shallowRef({ start: 0, end: 16 })
const columnRange = shallowRef({ start: 0, end: 8 })
const freezeColumns = ref(true)
const focusedRow = ref(-1)
const focusedColumn = ref(-1)
const offsets = computed(() => columnOffsets(props.columns))
const fixedWidth = computed(() => props.fixedWidths.reduce((sum, width) => sum + width, 0))
const totalWidth = computed(() => fixedWidth.value + offsets.value.at(-1)!)
const rows = computed(() => virtualEntries(props.rows, {
  start: Math.min(rowRange.value.start, props.rows.length), end: Math.min(rowRange.value.end, props.rows.length),
}, index => index * props.rowHeight, focusedRow.value))
const columns = computed(() => virtualEntries(props.columns, {
  start: Math.min(columnRange.value.start, props.columns.length), end: Math.min(columnRange.value.end, props.columns.length),
}, index => offsets.value[index]!, focusedColumn.value))
const afterHeight = computed(() => (props.rows.length - (rows.value.at(-1)?.index ?? -1) - 1) * props.rowHeight)
const afterWidth = computed(() => offsets.value.at(-1)! - offsets.value[(columns.value.at(-1)?.index ?? -1) + 1]!)
const groups = computed(() => {
  const result: Array<{ item: Column; index: number; before: number; count: number; width: number }> = []
  for (const entry of columns.value) {
    const previous = result.at(-1)
    if (previous && !entry.before && entry.item.groupKey && previous.item.groupKey === entry.item.groupKey) {
      previous.count++
      previous.width += entry.item.width
    }
    else result.push({ ...entry, count: 1, width: entry.item.width })
  }
  return result
})
const renderedColumnCount = computed(() => props.fixedWidths.length + columns.value.length
  + columns.value.filter(column => column.before > 0).length + (afterWidth.value > 0 ? 1 : 0))
let parent: HTMLElement | null = null
let observer: ResizeObserver | null = null
let frame = 0

function measure() {
  frame = 0
  if (!root.value || !table.value || !parent) return
  // Batch geometry reads before changing either window; no per-cell measurements.
  const bounds = root.value.getBoundingClientRect()
  const parentBounds = parent.getBoundingClientRect()
  const headerHeight = table.value.tHead?.offsetHeight ?? 0
  const top = parentBounds.top - bounds.top - headerHeight
  const nextRows = virtualRange(props.rows.length, props.rowHeight, top, parent.clientHeight)
  const width = root.value.clientWidth
  const frozen = width >= fixedWidth.value + 112
  const nextColumns = virtualColumnRange(offsets.value,
    frozen ? root.value.scrollLeft : Math.max(0, root.value.scrollLeft - fixedWidth.value),
    frozen ? width - fixedWidth.value : width)
  freezeColumns.value = frozen
  if (rowRange.value.start !== nextRows.start || rowRange.value.end !== nextRows.end) rowRange.value = nextRows
  if (columnRange.value.start !== nextColumns.start || columnRange.value.end !== nextColumns.end) columnRange.value = nextColumns
}
function schedule() { if (!frame) frame = requestAnimationFrame(measure) }
function retainFocus() {
  const target = document.activeElement
  const row = target instanceof Element && root.value?.contains(target) ? target.closest<HTMLElement>('[data-virtual-row]') : null
  const column = target instanceof Element && root.value?.contains(target) ? target.closest<HTMLElement>('[data-virtual-column]') : null
  focusedRow.value = row ? Number(row.dataset.virtualRow) : -1
  focusedColumn.value = column ? Number(column.dataset.virtualColumn) : -1
}
function releaseFocus() { void nextTick(retainFocus) }
watch(() => [props.rows, props.columns], () => {
  focusedRow.value = -1
  focusedColumn.value = -1
  void nextTick(schedule)
})
onMounted(() => {
  parent = root.value?.parentElement?.closest<HTMLElement>('[data-scroll-surface][data-scroll-axis="y"], [data-scroll-surface][data-scroll-axis="both"]') ?? null
  parent?.addEventListener('scroll', schedule, { passive: true })
  observer = new ResizeObserver(schedule)
  if (root.value) observer.observe(root.value)
  if (parent) observer.observe(parent)
  if (table.value?.tHead) observer.observe(table.value.tHead)
  schedule()
})
onBeforeUnmount(() => {
  if (frame) cancelAnimationFrame(frame)
  observer?.disconnect()
  parent?.removeEventListener('scroll', schedule)
})
</script>

<template>
  <div ref="root" v-scroll-surface data-slot="table-container" data-virtual-table data-scroll-surface
    :data-freeze-columns="freezeColumns" data-scroll-axis="x" data-pin-horizontal-scrollbar="nearest-y" class="relative w-full overflow-x-auto"
    @scroll.passive="schedule" @focusin="retainFocus" @focusout="releaseFocus">
    <table ref="table" data-slot="table" class="caption-bottom table-fixed text-sm"
      :style="{ width: `${totalWidth}px`, '--virtual-row-height': `${rowHeight}px` }"
      :aria-rowcount="rows.length ? props.rows.length + (table?.tHead?.rows.length ?? 1) : 1"
      :aria-colcount="fixedWidths.length + props.columns.length">
      <colgroup>
        <col v-for="(width, index) in fixedWidths" :key="index" :style="{ width: `${width}px` }">
        <template v-for="column in columns" :key="column.item.key">
          <col v-if="column.before" :style="{ width: `${column.before}px` }">
          <col :style="{ width: `${column.item.width}px` }">
        </template>
        <col v-if="afterWidth" :style="{ width: `${afterWidth}px` }">
      </colgroup>
      <slot :rows="rows" :columns="columns" :groups="groups" :after-height="afterHeight" :after-width="afterWidth"
        :rendered-column-count="renderedColumnCount" />
    </table>
  </div>
</template>
