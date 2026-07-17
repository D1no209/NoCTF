<script setup lang="ts">
import type { AwdpScreenEvent } from '@/types/awdpScreen'
import { BellRing, ChevronRight } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  events: AwdpScreenEvent[]
}>()

const visibleEvents = computed(() => props.events.slice(0, 9))

function toneFor(level: AwdpScreenEvent['level']) {
  if (level === 'success')
    return 'success' as const
  if (level === 'warning')
    return 'warning' as const
  if (level === 'danger')
    return 'danger' as const
  return 'info' as const
}

function formatTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime()))
    return '--:--:--'
  return new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(date)
}
</script>

<template>
  <CommandPanel class="awdp-event-feed">
    <header class="awdp-event-feed__header">
      <div>
        <CommandSignal label="Live event stream" tone="success" />
        <h2>Operations feed</h2>
      </div>
      <BellRing class="size-4 text-[var(--v2-cyan)]" />
    </header>

    <div v-if="visibleEvents.length === 0" class="awdp-event-feed__empty">
      Waiting for the first event.
    </div>

    <div v-else class="awdp-event-feed__rows">
      <article v-for="event in visibleEvents" :key="event.id" class="awdp-event-feed__row">
        <span class="awdp-event-feed__time">{{ formatTime(event.createdAt) }}</span>
        <span class="awdp-event-feed__line" :class="`awdp-event-feed__line--${toneFor(event.level)}`" aria-hidden="true" />
        <div class="awdp-event-feed__content">
          <CommandSignal :label="event.type.replace(/_/g, ' ')" :tone="toneFor(event.level)" />
          <p>{{ event.message }}</p>
          <small>R{{ event.round }} / {{ event.teamName || 'SYSTEM' }}{{ event.challengeName ? ` > ${event.challengeName}` : '' }}</small>
        </div>
        <ChevronRight class="awdp-event-feed__arrow size-4" />
      </article>
    </div>
  </CommandPanel>
</template>

<style scoped>
.awdp-event-feed { display: flex; min-height: 0; flex-direction: column; }
.awdp-event-feed__header { display: flex; min-height: 72px; align-items: center; justify-content: space-between; padding: 16px 18px 8px; }
.awdp-event-feed__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.awdp-event-feed__rows { padding: 0 18px 10px; }
.awdp-event-feed__row { display: grid; min-height: 68px; grid-template-columns: 68px 4px minmax(0, 1fr) 16px; align-items: center; gap: 10px; box-shadow: inset 0 -2px 0 rgb(184 188 194 / 0.5), inset 0 -1px 0 rgb(255 255 255 / 0.85); }
.awdp-event-feed__row:last-child { box-shadow: none; }
.awdp-event-feed__time { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; }
.awdp-event-feed__line { height: 32px; border-radius: 999px; background: var(--v2-info); }
.awdp-event-feed__line--success { background: var(--v2-cyan); }
.awdp-event-feed__line--warning { background: var(--v2-warning); }
.awdp-event-feed__line--danger { background: var(--v2-danger); }
.awdp-event-feed__content { min-width: 0; }
.awdp-event-feed__content p { overflow: hidden; margin: 5px 0 0; color: var(--v2-text); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.awdp-event-feed__content small { display: block; overflow: hidden; margin-top: 4px; color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; text-overflow: ellipsis; white-space: nowrap; }
.awdp-event-feed__arrow { color: var(--v2-text-faint); }
.awdp-event-feed__empty { display: grid; min-height: 210px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 13px; }

@media (max-width: 560px) {
  .awdp-event-feed__row { grid-template-columns: 58px 4px minmax(0, 1fr); }
  .awdp-event-feed__arrow { display: none; }
}
</style>
