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
.awdp-event-feed__header { display: flex; min-height: 68px; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--v2-line); padding: 12px 14px; }
.awdp-event-feed__header h2 { margin: 5px 0 0; font-size: 16px; font-weight: 650; }
.awdp-event-feed__rows { padding: 0 14px; }
.awdp-event-feed__row { display: grid; min-height: 68px; grid-template-columns: 68px 2px minmax(0, 1fr) 16px; align-items: center; gap: 9px; border-bottom: 1px solid rgb(26 58 103 / 0.65); }
.awdp-event-feed__row:last-child { border-bottom: 0; }
.awdp-event-feed__time { color: var(--v2-text-faint); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 10px; }
.awdp-event-feed__line { height: 32px; background: var(--v2-primary); }
.awdp-event-feed__line--success { background: var(--v2-cyan); box-shadow: 0 0 9px rgb(34 245 199 / 0.52); }
.awdp-event-feed__line--warning { background: var(--v2-warning); }
.awdp-event-feed__line--danger { background: var(--v2-danger); box-shadow: 0 0 9px rgb(255 84 112 / 0.52); }
.awdp-event-feed__content { min-width: 0; }
.awdp-event-feed__content p { overflow: hidden; margin: 4px 0 0; color: var(--v2-text); font-size: 11px; text-overflow: ellipsis; white-space: nowrap; }
.awdp-event-feed__content small { display: block; overflow: hidden; margin-top: 3px; color: var(--v2-text-faint); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 9px; text-overflow: ellipsis; white-space: nowrap; }
.awdp-event-feed__arrow { color: var(--v2-text-faint); }
.awdp-event-feed__empty { display: grid; min-height: 210px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 12px; }

@media (max-width: 560px) {
  .awdp-event-feed__row { grid-template-columns: 58px 2px minmax(0, 1fr); }
  .awdp-event-feed__arrow { display: none; }
}
</style>
