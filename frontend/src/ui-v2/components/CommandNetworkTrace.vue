<script setup lang="ts">
import { Crosshair, RadioTower } from 'lucide-vue-next'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import type { CommandTrace } from '../mock/shared-theme-mock'

defineProps<{
  trace: CommandTrace
}>()
</script>

<template>
  <CommandPanel class="network-trace" tone="signal">
    <header class="network-trace__header">
      <div>
        <CommandSignal :label="trace.roundLabel" tone="warning" />
        <h2>Attack surface trace</h2>
      </div>
      <Crosshair class="size-4 text-[var(--v2-cyan)]" />
    </header>
    <div class="network-trace__canvas" aria-label="Attack surface trace visualisation">
      <span class="network-trace__line network-trace__line--one" />
      <span class="network-trace__line network-trace__line--two" />
      <span class="network-trace__line network-trace__line--three" />
      <div class="network-trace__node network-trace__node--origin"><RadioTower class="size-4" /><small>CORE</small></div>
      <div
        v-for="(node, index) in trace.nodes"
        :key="node.name"
        class="network-trace__node"
        :class="[`network-trace__node--${index + 1}`, `network-trace__node--${node.status}`]"
      >
        <i />
        <small>{{ node.name }}</small>
      </div>
    </div>
    <footer class="network-trace__footer">
      <span>{{ String(trace.serviceCount).padStart(2, '0') }} challenges tracked</span>
      <span>{{ String(trace.activeTeamCount).padStart(2, '0') }} active teams</span>
      <span :class="{ 'network-trace__danger': trace.incidentCount > 0 }">{{ String(trace.incidentCount).padStart(2, '0') }} service alerts</span>
    </footer>
  </CommandPanel>
</template>

<style scoped>
.network-trace { display: flex; min-height: 300px; flex-direction: column; }
.network-trace__header { display: flex; min-height: 76px; align-items: center; justify-content: space-between; padding: 16px 18px 10px; }
.network-trace__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.network-trace__canvas { position: relative; min-height: 230px; flex: 1; overflow: hidden; margin: 0 18px; border-radius: 12px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.network-trace__line { position: absolute; display: block; height: 2px; border-radius: 999px; transform-origin: left; background: var(--v2-info); opacity: 0.55; }
.network-trace__line--one { top: 50%; left: 25%; width: 34%; transform: rotate(-28deg); }
.network-trace__line--two { top: 50%; left: 25%; width: 34%; transform: rotate(31deg); background: var(--v2-cyan); }
.network-trace__line--three { top: 50%; left: 25%; width: 24%; transform: rotate(2deg); background: var(--v2-magenta); }
.network-trace__node { position: absolute; display: grid; min-width: 72px; place-items: center; gap: 5px; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 10px; }
.network-trace__node i { width: 12px; height: 12px; border-radius: 999px; background: var(--v2-info); box-shadow: var(--v2-raised-sm); }
.network-trace__node small { font-size: 9px; }
.network-trace__node--origin { top: calc(50% - 25px); left: 17%; width: 48px; min-width: 48px; height: 48px; border-radius: 999px; place-items: center; color: var(--v2-cyan); background: var(--v2-surface); box-shadow: var(--v2-raised); }
.network-trace__node--1 { top: 14%; left: 59%; }
.network-trace__node--2 { top: 47%; left: 58%; }
.network-trace__node--3 { top: 65%; left: 70%; }
.network-trace__node--4 { top: 16%; left: 82%; }
.network-trace__node--stable { color: var(--v2-cyan); }
.network-trace__node--stable i { background: var(--v2-cyan); }
.network-trace__node--degraded { color: var(--v2-warning); }
.network-trace__node--degraded i { background: var(--v2-warning); }
.network-trace__node--critical { color: var(--v2-danger); }
.network-trace__node--critical i { background: var(--v2-danger); }
.network-trace__footer { display: flex; flex-wrap: wrap; gap: 12px 20px; padding: 14px 18px 16px; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 10px; font-weight: 600; letter-spacing: 0.04em; }
.network-trace__danger { color: var(--v2-danger); }

@media (max-width: 560px) {
  .network-trace__node--4 { left: 76%; }
  .network-trace__node--3 { left: 62%; }
}
</style>
