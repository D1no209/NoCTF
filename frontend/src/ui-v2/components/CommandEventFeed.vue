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
  min-height: 72px;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--v2-line);
  padding: 14px 16px;
}

.event-feed__header h2 {
  margin: 5px 0 0;
  font-size: 16px;
  font-weight: 650;
}

.event-feed__rows { padding: 0 16px; }

.event-feed__row {
  display: grid;
  min-height: 61px;
  grid-template-columns: 70px 2px minmax(0, 1fr) 18px;
  align-items: center;
  gap: 10px;
  border-bottom: 1px solid rgb(26 58 103 / 0.7);
}

.event-feed__row:last-child { border-bottom: 0; }

.event-feed__time {
  color: var(--v2-text-faint);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 11px;
}

.event-feed__line {
  height: 26px;
  background: var(--v2-primary);
  box-shadow: 0 0 12px rgb(47 140 255 / 0.55);
}

.event-feed__line--success { background: var(--v2-cyan); box-shadow: 0 0 12px rgb(34 245 199 / 0.55); }
.event-feed__line--warning { background: var(--v2-warning); box-shadow: 0 0 12px rgb(255 209 102 / 0.45); }
.event-feed__line--danger { background: var(--v2-danger); box-shadow: 0 0 12px rgb(255 84 112 / 0.5); }

.event-feed__content { min-width: 0; }
.event-feed__content strong { display: block; font-size: 11px; letter-spacing: 0.06em; }
.event-feed__content p { overflow: hidden; margin: 3px 0 0; color: var(--v2-text-muted); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.event-feed__kind--success { color: var(--v2-cyan); }
.event-feed__kind--warning { color: var(--v2-warning); }
.event-feed__kind--danger { color: var(--v2-danger); }
.event-feed__kind--info { color: var(--v2-primary); }
.event-feed__arrow { color: var(--v2-text-faint); }

@media (max-width: 560px) {
  .event-feed__row { grid-template-columns: 58px 2px minmax(0, 1fr); }
  .event-feed__arrow { display: none; }
}
</style>
