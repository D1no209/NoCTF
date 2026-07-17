<script setup lang="ts">
import { BellRing, ChevronRight } from 'lucide-vue-next'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import type { CommandEvent } from '../mock/shared-theme-mock'

defineProps<{
  events: CommandEvent[]
}>()
</script>

<template>
  <CommandPanel class="event-feed">
    <header class="event-feed__header">
      <div>
        <CommandSignal label="Live event stream" tone="success" />
        <h2>Operations feed</h2>
      </div>
      <BellRing class="size-4 text-[var(--v2-cyan)]" />
    </header>
    <div class="event-feed__rows">
      <article v-for="event in events" :key="event.id" class="event-feed__row">
        <span class="event-feed__time">{{ event.time }}</span>
        <span class="event-feed__line" :class="`event-feed__line--${event.tone}`" aria-hidden="true" />
        <div class="event-feed__content">
          <strong :class="`event-feed__kind--${event.tone}`">{{ event.kind }}</strong>
          <p>{{ event.message }}</p>
        </div>
        <ChevronRight class="event-feed__arrow size-4" />
      </article>
    </div>
  </CommandPanel>
</template>

<style scoped>
.event-feed {
  display: flex;
  min-height: 0;
  flex-direction: column;
}

.event-feed__header {
  display: flex;
  min-height: 76px;
  align-items: center;
  justify-content: space-between;
  padding: 16px 18px 10px;
}

.event-feed__header h2 {
  margin: 7px 0 0;
  color: var(--v2-text);
  font-size: 17px;
  font-weight: 600;
}

.event-feed__rows { padding: 0 18px 10px; }

.event-feed__row {
  display: grid;
  min-height: 60px;
  grid-template-columns: 70px 4px minmax(0, 1fr) 18px;
  align-items: center;
  gap: 12px;
  box-shadow: inset 0 -2px 0 rgb(184 188 194 / 0.5), inset 0 -1px 0 rgb(255 255 255 / 0.85);
}

.event-feed__row:last-child { box-shadow: none; }

.event-feed__time {
  color: var(--v2-text-faint);
  font-family: var(--v2-font-mono);
  font-size: 11px;
}

.event-feed__line {
  height: 26px;
  border-radius: 999px;
  background: var(--v2-info);
}

.event-feed__line--success { background: var(--v2-cyan); }
.event-feed__line--warning { background: var(--v2-warning); }
.event-feed__line--danger { background: var(--v2-danger); }

.event-feed__content { min-width: 0; }
.event-feed__content strong { display: block; font-size: 12px; font-weight: 600; letter-spacing: 0.02em; }
.event-feed__content p { overflow: hidden; margin: 3px 0 0; color: var(--v2-text-muted); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.event-feed__kind--success { color: var(--v2-cyan); }
.event-feed__kind--warning { color: var(--v2-warning); }
.event-feed__kind--danger { color: var(--v2-danger); }
.event-feed__kind--info { color: var(--v2-info); }
.event-feed__arrow { color: var(--v2-text-faint); }

@media (max-width: 560px) {
  .event-feed__row { grid-template-columns: 58px 4px minmax(0, 1fr); }
  .event-feed__arrow { display: none; }
}
</style>
