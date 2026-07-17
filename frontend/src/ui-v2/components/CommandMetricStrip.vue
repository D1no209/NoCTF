<script setup lang="ts">
import { Activity, AlertTriangle, Flag, UsersRound } from 'lucide-vue-next'
import CommandPanel from '../primitives/CommandPanel.vue'
import type { CommandMetric } from '../mock/shared-theme-mock'

defineProps<{
  metrics: CommandMetric[]
}>()

const icons = [Activity, UsersRound, Flag, AlertTriangle]
</script>

<template>
  <div class="metric-strip">
    <CommandPanel v-for="(metric, index) in metrics" :key="metric.label" class="metric-strip__item" :tone="metric.tone === 'success' ? 'signal' : metric.tone === 'warning' || metric.tone === 'danger' ? 'warning' : 'primary'">
      <component :is="icons[index]" class="metric-strip__icon" :class="`metric-strip__icon--${metric.tone}`" />
      <div class="metric-strip__body">
        <span>{{ metric.label }}</span>
        <strong>{{ metric.value }}</strong>
        <small :class="`metric-strip__delta--${metric.tone}`">{{ metric.delta }}</small>
      </div>
    </CommandPanel>
  </div>
</template>

<style scoped>
.metric-strip {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.metric-strip__item {
  display: flex;
  min-height: 100px;
  align-items: flex-start;
  gap: 12px;
  padding: 14px;
}

.metric-strip__icon {
  width: 18px;
  height: 18px;
  margin-top: 3px;
  color: var(--v2-primary);
}

.metric-strip__icon--success { color: var(--v2-cyan); }
.metric-strip__icon--warning { color: var(--v2-warning); }
.metric-strip__icon--danger { color: var(--v2-danger); }

.metric-strip__body {
  display: grid;
  gap: 3px;
  min-width: 0;
}

.metric-strip__body span,
.metric-strip__body small {
  color: var(--v2-text-muted);
  font-size: 11px;
  font-weight: 600;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.metric-strip__body strong {
  color: var(--v2-text);
  font-size: 28px;
  font-weight: 650;
  line-height: 1;
  letter-spacing: 0;
}

.metric-strip__delta--success { color: var(--v2-cyan) !important; }
.metric-strip__delta--warning { color: var(--v2-warning) !important; }
.metric-strip__delta--danger { color: var(--v2-danger) !important; }

@media (max-width: 960px) {
  .metric-strip { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 560px) {
  .metric-strip { grid-template-columns: 1fr; }
}
</style>
