<script setup lang="ts">
import { Network } from 'lucide-vue-next'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import type { CommandService } from '../mock/shared-theme-mock'

defineProps<{
  services: CommandService[]
}>()

function toneFor(status: CommandService['status']) {
  if (status === 'stable')
    return 'success'
  if (status === 'degraded')
    return 'warning'
  return 'danger'
}
</script>

<template>
  <CommandPanel class="service-matrix">
    <header class="service-matrix__header">
      <div>
        <CommandSignal label="Service mesh" tone="info" />
        <h2>Infrastructure matrix</h2>
      </div>
      <Network class="size-4 text-[var(--v2-primary)]" />
    </header>
    <div class="service-matrix__grid">
      <article v-for="service in services" :key="service.name" class="service-matrix__cell">
        <div class="service-matrix__top">
          <strong>{{ service.name }}</strong>
          <CommandSignal :label="service.status" :tone="toneFor(service.status)" />
        </div>
        <div class="service-matrix__details">
          <span>{{ service.category }}</span>
          <b>{{ service.activity }}</b>
        </div>
      </article>
    </div>
  </CommandPanel>
</template>

<style scoped>
.service-matrix__header {
  display: flex;
  min-height: 76px;
  align-items: center;
  justify-content: space-between;
  padding: 16px 18px 10px;
}

.service-matrix__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.service-matrix__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; padding: 6px 18px 18px; }
.service-matrix__cell { min-width: 0; border-radius: 12px; padding: 13px 14px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.service-matrix__top, .service-matrix__details { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
.service-matrix__top strong { overflow: hidden; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.service-matrix__details { margin-top: 9px; color: var(--v2-text-muted); font-size: 11px; }
.service-matrix__details b { color: var(--v2-text); font-family: var(--v2-font-mono); font-weight: 600; }

@media (max-width: 560px) {
  .service-matrix__grid { grid-template-columns: 1fr; }
}
</style>
