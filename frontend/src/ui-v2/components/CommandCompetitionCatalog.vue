<script setup lang="ts">
import { AlertTriangle, Inbox, RefreshCw, SearchX } from 'lucide-vue-next'
import CommandButton from '../primitives/CommandButton.vue'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSelect, { type CommandSelectOption } from '../primitives/CommandSelect.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import CommandCompetitionCard, { type CommandCompetition } from './CommandCompetitionCard.vue'

type CatalogState = 'loading' | 'error' | 'empty' | 'filtered-empty' | 'ready'

const props = defineProps<{
  competitions: CommandCompetition[]
  state: CatalogState
  refreshing: boolean
  search: string
  statusFilter: string
  modeFilter: string
}>()

defineEmits<{
  'update:search': [value: string]
  'update:statusFilter': [value: string]
  'update:modeFilter': [value: string]
  refresh: []
  reset: []
  open: [id: string]
  register: [id: string]
}>()

const statusOptions: CommandSelectOption[] = [
  { value: 'all', label: 'All statuses' },
  { value: 'draft', label: 'Draft' },
  { value: 'published', label: 'Published' },
  { value: 'running', label: 'Running' },
  { value: 'finished', label: 'Finished' },
]

const modeOptions: CommandSelectOption[] = [
  { value: 'all', label: 'All modes' },
  { value: 'ctf', label: 'CTF' },
  { value: 'awd', label: 'AWD' },
  { value: 'awdp', label: 'AWDP' },
  { value: 'koh', label: 'KoH' },
  { value: 'penetration', label: 'Penetration' },
]
</script>

<template>
  <section class="competition-catalog">
    <CommandPanel class="competition-catalog__filters">
      <div class="competition-catalog__search">
        <SearchX class="size-4" />
        <CommandInput
          :model-value="props.search"
          label="Search competitions"
          placeholder="Search title or brief"
          @update:model-value="$emit('update:search', $event)"
        />
      </div>
      <CommandSelect
        :model-value="props.statusFilter"
        label="Filter by competition status"
        :options="statusOptions"
        @update:model-value="$emit('update:statusFilter', $event)"
      />
      <CommandSelect
        :model-value="props.modeFilter"
        label="Filter by game mode"
        :options="modeOptions"
        @update:model-value="$emit('update:modeFilter', $event)"
      />
      <CommandIconButton :icon="RefreshCw" :label="props.refreshing ? 'Refreshing competitions' : 'Refresh competitions'" compact @click="$emit('refresh')" />
    </CommandPanel>

    <div v-if="props.state === 'loading'" class="competition-catalog__grid" aria-busy="true">
      <CommandPanel v-for="item in 6" :key="item" class="competition-catalog__skeleton">
        <span />
        <span />
        <span />
        <span />
      </CommandPanel>
    </div>

    <CommandPanel v-else-if="props.state === 'error'" class="competition-catalog__state">
      <AlertTriangle class="size-6 text-[var(--v2-danger)]" />
      <div>
        <CommandSignal label="Competition service unavailable" tone="danger" />
        <h2>Unable to load competitions</h2>
        <p>The service did not return a usable competition list.</p>
      </div>
      <CommandButton label="Retry" tone="outline" @click="$emit('refresh')" />
    </CommandPanel>

    <CommandPanel v-else-if="props.state === 'empty'" class="competition-catalog__state">
      <Inbox class="size-6 text-[var(--v2-text-muted)]" />
      <div>
        <CommandSignal label="No records" tone="info" />
        <h2>No competitions published</h2>
        <p>There are no competition records available from the service.</p>
      </div>
      <CommandButton label="Refresh" tone="outline" @click="$emit('refresh')" />
    </CommandPanel>

    <CommandPanel v-else-if="props.state === 'filtered-empty'" class="competition-catalog__state">
      <SearchX class="size-6 text-[var(--v2-warning)]" />
      <div>
        <CommandSignal label="No match" tone="warning" />
        <h2>No competitions match these filters</h2>
        <p>Clear the search or adjust the status and mode filters.</p>
      </div>
      <CommandButton label="Reset filters" tone="outline" @click="$emit('reset')" />
    </CommandPanel>

    <div v-else class="competition-catalog__grid">
      <CommandCompetitionCard
        v-for="competition in props.competitions"
        :key="competition.id"
        :competition="competition"
        @open="$emit('open', $event)"
        @register="$emit('register', $event)"
      />
    </div>
  </section>
</template>

<style scoped>
.competition-catalog { display: grid; gap: 16px; }

.competition-catalog__filters {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 180px 160px 38px;
  align-items: center;
  gap: 10px;
  padding: 14px;
}

.competition-catalog__search { position: relative; min-width: 0; }
.competition-catalog__search > svg { position: absolute; z-index: 1; top: 11px; left: 12px; color: var(--v2-text-faint); pointer-events: none; }
.competition-catalog__search :deep(.command-input) { padding-left: 34px; }
.competition-catalog__grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }

.competition-catalog__skeleton {
  display: grid;
  min-height: 286px;
  grid-template-rows: 56px 1fr 52px 54px;
  gap: 14px;
  padding: 18px;
}

.competition-catalog__skeleton span {
  display: block;
  border-radius: 12px;
  background: var(--v2-surface-strong);
  box-shadow: var(--v2-inset);
  animation: command-skeleton 1.4s ease-in-out infinite alternate;
}

.competition-catalog__skeleton span:nth-child(3) { width: 74%; }
.competition-catalog__state { display: grid; min-height: 180px; grid-template-columns: auto minmax(0, 1fr) auto; align-items: center; gap: 18px; padding: 26px; }
.competition-catalog__state h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.competition-catalog__state p { margin: 6px 0 0; color: var(--v2-text-muted); font-size: 13px; }

@keyframes command-skeleton {
  from { opacity: 1; }
  to { opacity: 0.45; }
}

@media (max-width: 1120px) {
  .competition-catalog__grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 760px) {
  .competition-catalog__filters { grid-template-columns: minmax(0, 1fr) 38px; }
  .competition-catalog__search { grid-column: 1 / -1; }
  .competition-catalog__state { grid-template-columns: auto minmax(0, 1fr); }
  .competition-catalog__state :deep(.command-button) { grid-column: 2; justify-self: start; }
}

@media (max-width: 560px) {
  .competition-catalog__grid { grid-template-columns: 1fr; }
}
</style>
