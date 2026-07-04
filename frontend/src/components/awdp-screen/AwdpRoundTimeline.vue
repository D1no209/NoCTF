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
</script>

<template>
  <section class="awdp-panel flex min-h-0 flex-col">
    <div class="flex items-center justify-between border-b border-slate-200/10 px-4 py-2">
      <div>
        <h2 class="text-sm font-semibold text-slate-100">
          Round timeline
        </h2>
        <p class="text-xs text-slate-500">
          Recent attack and defense intensity
        </p>
      </div>
      <Timer class="size-5 text-slate-300" />
    </div>

    <div v-if="visibleRounds.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-slate-500">
      No round data yet.
    </div>

    <div v-else class="grid min-h-0 flex-1 grid-cols-8 gap-1.5 p-2 xl:grid-cols-16">
      <article
        v-for="round in visibleRounds"
        :key="round.round"
        class="timeline-cell"
        :class="round.round === currentRound ? 'timeline-cell-current' : ''"
      >
        <div class="flex items-center justify-between gap-2">
          <span class="font-mono text-xs font-semibold text-slate-200">R{{ round.round }}</span>
          <span class="font-mono text-[10px] text-slate-500">{{ round.activeTeamCount }}</span>
        </div>
        <div class="mt-2 flex h-12 items-end gap-1">
          <div
            class="timeline-bar bg-cyan-200"
            :style="{ height: `${Math.max(8, (round.attackSuccessCount / maxEvents) * 100)}%` }"
            title="Attack success"
          />
          <div
            class="timeline-bar bg-orange-200"
            :style="{ height: `${Math.max(8, (round.attackFailCount / maxEvents) * 100)}%` }"
            title="Attack failed"
          />
          <div
            class="timeline-bar bg-emerald-200"
            :style="{ height: `${Math.max(8, (round.defenseSuccessCount / maxEvents) * 100)}%` }"
            title="Defense success"
          />
          <div
            class="timeline-bar bg-rose-200"
            :style="{ height: `${Math.max(8, (round.defenseFailCount / maxEvents) * 100)}%` }"
            title="Defense failed"
          />
        </div>
        <div class="mt-2 font-mono text-[10px] text-slate-400">
          {{ round.scoreDelta > 0 ? '+' : '' }}{{ round.scoreDelta }}
        </div>
      </article>
    </div>
  </section>
</template>

<style scoped>
.timeline-cell {
  min-width: 0;
  border: 1px solid rgb(148 163 184 / 0.12);
  border-radius: 0.42rem;
  background: rgb(2 6 23 / 0.28);
  padding: 0.45rem;
}

.timeline-cell-current {
  border-color: rgb(203 213 225 / 0.34);
  background: rgb(15 23 42 / 0.72);
}

.timeline-bar {
  width: 100%;
  min-height: 0.5rem;
  border-radius: 999px 999px 0.2rem 0.2rem;
  opacity: 0.9;
  transition: height 240ms cubic-bezier(0.16, 1, 0.3, 1);
}
</style>
