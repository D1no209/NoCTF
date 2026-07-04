<script setup lang="ts">
import type { AwdpRoundStat } from '@/types/awdpScreen'
import { Timer } from 'lucide-vue-next'
import { computed } from 'vue'

const props = defineProps<{
  rounds: AwdpRoundStat[]
  currentRound: number
}>()

const visibleRounds = computed(() => props.rounds.slice(-16))
const maxEvents = computed(() => Math.max(1, ...visibleRounds.value.map(round =>
  round.attackSuccessCount + round.attackFailCount + round.defenseSuccessCount + round.defenseFailCount,
)))

const midEvents = computed(() => Math.ceil(maxEvents.value / 2))
</script>

<template>
  <section class="awdp-panel timeline-panel">
    <div class="timeline-head">
      <div>
        <h2>
          Round timeline
        </h2>
        <p>
          Recent break and fix intensity
        </p>
      </div>
      <Timer class="size-4" />
    </div>

    <div v-if="visibleRounds.length === 0" class="timeline-empty">
      No round data yet.
    </div>

    <div v-else class="timeline-body">
      <div class="timeline-axis">
        <span>{{ maxEvents }}</span>
        <span>{{ midEvents }}</span>
        <span>0</span>
      </div>

      <div class="timeline-plot">
        <div class="timeline-grid-line line-top" />
        <div class="timeline-grid-line line-mid" />
        <div class="timeline-grid-line line-bottom" />

        <article
          v-for="round in visibleRounds"
          :key="round.round"
          class="timeline-cell"
          :class="round.round === currentRound ? 'timeline-cell-current' : ''"
        >
          <div class="timeline-cell-top">
            <span>R{{ round.round }}</span>
            <span>{{ round.activeTeamCount }}</span>
          </div>
          <div class="timeline-bars">
            <div
              class="timeline-bar bar-break"
              :style="{ height: `${Math.max(8, (round.attackSuccessCount / maxEvents) * 100)}%` }"
              title="Break success"
            />
            <div
              class="timeline-bar bar-break-fail"
              :style="{ height: `${Math.max(8, (round.attackFailCount / maxEvents) * 100)}%` }"
              title="Break failed"
            />
            <div
              class="timeline-bar bar-fix"
              :style="{ height: `${Math.max(8, (round.defenseSuccessCount / maxEvents) * 100)}%` }"
              title="Fix success"
            />
            <div
              class="timeline-bar bar-fix-fail"
              :style="{ height: `${Math.max(8, (round.defenseFailCount / maxEvents) * 100)}%` }"
              title="Fix failed"
            />
          </div>
          <div class="timeline-score">
            {{ round.scoreDelta > 0 ? '+' : '' }}{{ round.scoreDelta }}
          </div>
        </article>
      </div>

      <div class="timeline-legend">
        <span><i class="bar-break" /> Break ok</span>
        <span><i class="bar-break-fail" /> Break fail</span>
        <span><i class="bar-fix" /> Fix ok</span>
        <span><i class="bar-fix-fail" /> Fix fail</span>
      </div>
    </div>
  </section>
</template>

<style scoped>
.timeline-panel {
  display: flex;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
}

.timeline-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-bottom: 1px solid var(--awdp-screen-border);
  padding: 0.62rem 0.82rem 0.55rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 58%, transparent);
}

.timeline-head h2 {
  margin: 0;
  color: var(--sidebar-foreground);
  font-size: 0.8rem;
  font-weight: 850;
  text-transform: uppercase;
}

.timeline-head p {
  margin: 0.12rem 0 0;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.66rem;
}

.timeline-empty {
  display: grid;
  flex: 1;
  place-items: center;
  color: color-mix(in oklch, var(--sidebar-foreground) 45%, transparent);
  font-size: 0.82rem;
}

.timeline-body {
  display: grid;
  grid-template-columns: 2.1rem minmax(0, 1fr);
  grid-template-rows: minmax(0, 1fr) auto;
  min-height: 0;
  flex: 1;
  gap: 0.35rem 0.5rem;
  padding: 0.55rem 0.75rem 0.62rem;
}

.timeline-axis {
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 1.55rem 0 1.55rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 40%, transparent);
  font-size: 0.58rem;
  text-align: right;
}

.timeline-plot {
  position: relative;
  display: grid;
  grid-template-columns: repeat(16, minmax(0, 1fr));
  gap: 0.38rem;
  min-height: 0;
}

.timeline-grid-line {
  position: absolute;
  right: 0;
  left: 0;
  border-top: 1px dashed color-mix(in oklch, var(--sidebar-foreground) 16%, transparent);
  pointer-events: none;
}

.line-top {
  top: 1.58rem;
}

.line-mid {
  top: 50%;
}

.line-bottom {
  bottom: 1.58rem;
}

.timeline-cell {
  position: relative;
  z-index: 1;
  display: grid;
  grid-template-rows: auto minmax(0, 1fr) auto;
  min-width: 0;
  border: 1px solid color-mix(in oklch, var(--sidebar-foreground) 11%, transparent);
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--awdp-screen-panel-raised) 42%, var(--awdp-screen-bg));
  padding: 0.38rem;
}

.timeline-cell-current {
  border-color: color-mix(in oklch, var(--awdp-break) 34%, var(--awdp-screen-border));
  background: color-mix(in oklch, var(--awdp-break) 8%, var(--awdp-screen-panel-raised));
}

.timeline-cell-top {
  display: flex;
  justify-content: space-between;
  gap: 0.35rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 72%, transparent);
  font-size: 0.62rem;
  font-weight: 800;
}

.timeline-bars {
  display: flex;
  align-items: end;
  gap: 0.12rem;
  min-height: 0;
  padding-top: 0.35rem;
}

.timeline-bar {
  width: 100%;
  min-height: 0.4rem;
  border-radius: var(--radius-sm) var(--radius-sm) 0 0;
}

.bar-break {
  background: var(--awdp-break);
}

.bar-break-fail {
  background: var(--awdp-warn);
}

.bar-fix {
  background: var(--awdp-fix);
}

.bar-fix-fail {
  background: var(--awdp-error);
}

.timeline-score {
  margin-top: 0.32rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 58%, transparent);
  font-size: 0.6rem;
  font-weight: 700;
  text-align: center;
  font-variant-numeric: tabular-nums;
}

.timeline-legend {
  grid-column: 2;
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 44%, transparent);
  font-size: 0.58rem;
  font-weight: 800;
  text-transform: uppercase;
}

.timeline-legend span {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
}

.timeline-legend i {
  width: 0.42rem;
  height: 0.42rem;
  border-radius: 50%;
}

@media (max-width: 1280px) {
  .timeline-plot {
    grid-template-columns: repeat(8, minmax(0, 1fr));
  }
}
</style>
