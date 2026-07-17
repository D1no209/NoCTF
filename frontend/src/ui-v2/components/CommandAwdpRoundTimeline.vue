<script setup lang="ts">
import type { AwdpRoundStat } from '@/types/awdpScreen'
import { Timer } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  rounds: AwdpRoundStat[]
  currentRound: number
}>()

const visibleRounds = computed(() => props.rounds.slice(-12))
const maxActivity = computed(() => Math.max(1, ...visibleRounds.value.map(round =>
  round.attackSuccessCount + round.attackFailCount + round.defenseSuccessCount + round.defenseFailCount,
)))

function activity(round: AwdpRoundStat) {
  return round.attackSuccessCount + round.attackFailCount + round.defenseSuccessCount + round.defenseFailCount
}

function width(value: number) {
  return `${(value / maxActivity.value) * 100}%`
}
</script>

<template>
  <CommandPanel class="awdp-round-timeline">
    <header class="awdp-round-timeline__header">
      <div>
        <CommandSignal label="Round history" tone="info" />
        <h2>Attack and defense trace</h2>
      </div>
      <Timer class="size-4 text-[var(--v2-primary)]" />
    </header>

    <div v-if="visibleRounds.length === 0" class="awdp-round-timeline__empty">
      Round history will appear after telemetry arrives.
    </div>

    <div v-else class="awdp-round-timeline__rows">
      <article
        v-for="round in visibleRounds"
        :key="round.round"
        class="awdp-round-timeline__row"
        :class="{ 'awdp-round-timeline__row--current': round.round === currentRound }"
      >
        <strong>R{{ String(round.round).padStart(2, '0') }}</strong>
        <div class="awdp-round-timeline__bar">
          <i class="awdp-round-timeline__attack-success" :style="{ width: width(round.attackSuccessCount) }" />
          <i class="awdp-round-timeline__attack-fail" :style="{ width: width(round.attackFailCount) }" />
          <i class="awdp-round-timeline__defense-success" :style="{ width: width(round.defenseSuccessCount) }" />
          <i class="awdp-round-timeline__defense-fail" :style="{ width: width(round.defenseFailCount) }" />
        </div>
        <span>{{ activity(round) }} ops</span>
        <b :class="{ 'awdp-round-timeline__delta--positive': round.scoreDelta > 0, 'awdp-round-timeline__delta--negative': round.scoreDelta < 0 }">
          {{ round.scoreDelta > 0 ? '+' : '' }}{{ round.scoreDelta }}
        </b>
      </article>
    </div>

    <footer class="awdp-round-timeline__legend">
      <span><i class="awdp-round-timeline__attack-success" />Attack pass</span>
      <span><i class="awdp-round-timeline__attack-fail" />Attack fail</span>
      <span><i class="awdp-round-timeline__defense-success" />Defense pass</span>
      <span><i class="awdp-round-timeline__defense-fail" />Defense fail</span>
    </footer>
  </CommandPanel>
</template>

<style scoped>
.awdp-round-timeline__header { display: flex; min-height: 72px; align-items: center; justify-content: space-between; padding: 16px 18px 8px; }
.awdp-round-timeline__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.awdp-round-timeline__rows { display: grid; gap: 6px; padding: 8px 18px 12px; }
.awdp-round-timeline__row { display: grid; min-height: 32px; grid-template-columns: 40px minmax(90px, 1fr) 48px 58px; align-items: center; gap: 10px; border-radius: 10px; color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 11px; }
.awdp-round-timeline__row--current { background: var(--v2-surface); box-shadow: var(--v2-inset); }
.awdp-round-timeline__row strong { color: var(--v2-text); font-weight: 700; }
.awdp-round-timeline__row > span,
.awdp-round-timeline__row > b { text-align: right; font-weight: 600; }
.awdp-round-timeline__bar { display: flex; height: 12px; overflow: hidden; border-radius: 999px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); }
.awdp-round-timeline__bar i { display: block; height: 100%; }
.awdp-round-timeline__attack-success { background: var(--v2-primary); }
.awdp-round-timeline__attack-fail { background: var(--v2-warning); }
.awdp-round-timeline__defense-success { background: var(--v2-cyan); }
.awdp-round-timeline__defense-fail { background: var(--v2-danger); }
.awdp-round-timeline__delta--positive { color: var(--v2-cyan); }
.awdp-round-timeline__delta--negative { color: var(--v2-danger); }
.awdp-round-timeline__legend { display: flex; flex-wrap: wrap; gap: 8px 16px; padding: 4px 18px 16px; color: var(--v2-text-muted); font-size: 10px; }
.awdp-round-timeline__legend span { display: inline-flex; align-items: center; gap: 6px; }
.awdp-round-timeline__legend i { width: 10px; height: 6px; border-radius: 999px; }
.awdp-round-timeline__empty { display: grid; min-height: 180px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 13px; }

@media (max-width: 520px) {
  .awdp-round-timeline__row { grid-template-columns: 38px minmax(70px, 1fr) 52px; }
  .awdp-round-timeline__row > span { display: none; }
}
</style>
