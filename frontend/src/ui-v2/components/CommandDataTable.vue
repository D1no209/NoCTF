<script setup lang="ts">
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandDataTableColumn {
  key: string
  label: string
  align?: 'left' | 'center' | 'right'
  sortable?: boolean
}

const props = withDefaults(defineProps<{
  columns: CommandDataTableColumn[]
  rowCount?: number
  loading?: boolean
  emptyLabel?: string
  page?: number
  pageCount?: number
  totalCount?: number
  sortKey?: string
  sortDir?: 'asc' | 'desc'
}>(), {
  rowCount: undefined,
  loading: false,
  emptyLabel: 'No records available.',
  page: undefined,
  pageCount: undefined,
  totalCount: undefined,
  sortKey: undefined,
  sortDir: undefined,
})

const emit = defineEmits<{
  'update:page': [value: number]
  sort: [key: string]
}>()
</script>

<template>
  <CommandPanel class="command-data-table">
    <div v-if="props.loading" class="command-data-table__state" aria-busy="true">
      <CommandSignal label="Synchronizing" tone="info" />
      <span>Loading records.</span>
    </div>
    <div v-else-if="props.rowCount === 0" class="command-data-table__state">
      <CommandSignal label="Empty set" tone="info" />
      <span>{{ props.emptyLabel }}</span>
    </div>
    <div v-else class="command-data-table__scroll">
      <table>
        <thead>
          <tr>
            <th
              v-for="column in props.columns"
              :key="column.key"
              :style="{ textAlign: column.align || 'left' }"
            >
              <button
                v-if="column.sortable"
                type="button"
                class="command-data-table__sort"
                :class="{ 'command-data-table__sort--active': props.sortKey === column.key }"
                @click="emit('sort', column.key)"
              >
                {{ column.label }}
                <span v-if="props.sortKey === column.key">{{ props.sortDir === 'desc' ? '▼' : '▲' }}</span>
              </button>
              <template v-else>{{ column.label }}</template>
            </th>
          </tr>
        </thead>
        <tbody>
          <slot />
        </tbody>
      </table>
    </div>

    <footer v-if="props.page !== undefined && props.pageCount !== undefined" class="command-data-table__footer">
      <span>
        Page {{ props.page + 1 }} of {{ props.pageCount }}<template v-if="props.totalCount !== undefined && props.rowCount !== undefined"> · {{ props.rowCount }} of {{ props.totalCount }} records</template>
      </span>
      <div class="command-data-table__pager">
        <CommandButton label="Previous" tone="ghost" :disabled="props.page <= 0" @click="emit('update:page', props.page - 1)" />
        <CommandButton label="Next" tone="ghost" :disabled="props.page >= props.pageCount - 1" @click="emit('update:page', props.page + 1)" />
      </div>
    </footer>
  </CommandPanel>
</template>

<style scoped>
.command-data-table { overflow: hidden; }

.command-data-table__state { display: flex; min-height: 140px; flex-direction: column; align-items: flex-start; justify-content: center; gap: 9px; padding: 22px; color: var(--v2-text-muted); font-size: 13px; }

.command-data-table__scroll { overflow-x: auto; }

.command-data-table table { width: 100%; border-collapse: separate; border-spacing: 0; }

.command-data-table th {
  padding: 14px 16px 10px;
  color: var(--v2-text-faint);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.05em;
  text-align: left;
  white-space: nowrap;
}

.command-data-table__sort { display: inline-flex; align-items: center; gap: 5px; border: 0; padding: 0; background: transparent; color: inherit; cursor: pointer; font: inherit; letter-spacing: inherit; }
.command-data-table__sort:hover, .command-data-table__sort--active { color: var(--v2-text); }

.command-data-table :deep(tbody tr) { transition: background-color 160ms ease; }
.command-data-table :deep(tbody tr:hover) { background: var(--v2-surface-hover); }

.command-data-table :deep(td) {
  padding: 11px 16px;
  color: var(--v2-text-muted);
  font-size: 12px;
  vertical-align: middle;
  box-shadow: inset 0 1px 0 rgb(184 188 194 / 0.28), inset 0 -1px 0 rgb(255 255 255 / 0.55);
}

.command-data-table :deep(td .cell-strong) { color: var(--v2-text); font-weight: 600; }
.command-data-table :deep(td .cell-mono) { font-family: var(--v2-font-mono); font-size: 11px; }
.command-data-table :deep(td .cell-actions) { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 8px; }

.command-data-table__footer { display: flex; align-items: center; justify-content: space-between; gap: 14px; padding: 12px 16px 14px; box-shadow: inset 0 1px 0 rgb(255 255 255 / 0.6), inset 0 -1px 0 rgb(184 188 194 / 0.2); }
.command-data-table__footer > span { color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 10px; font-weight: 600; letter-spacing: 0.03em; }
.command-data-table__pager { display: flex; gap: 8px; }
</style>
