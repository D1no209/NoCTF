<script setup lang="ts">
import type { AwdpChallengeStatus } from '@/types/awdpScreen'
import { Blocks } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  challenges: AwdpChallengeStatus[]
}>()

const orderedChallenges = computed(() => [...props.challenges].sort((left, right) => right.attackHeat - left.attackHeat))

function categoryTone(category: AwdpChallengeStatus['category']) {
  if (category === 'pwn')
    return 'danger' as const
  if (category === 'crypto')
    return 'warning' as const
  if (category === 'reverse')
    return 'success' as const
  return 'info' as const
}
</script>

<template>
  <CommandPanel class="awdp-challenge-matrix" tone="signal">
    <header class="awdp-challenge-matrix__header">
      <div>
        <CommandSignal label="Challenge mesh" tone="info" />
        <h2>Service pressure</h2>
      </div>
      <Blocks class="size-4 text-[var(--v2-primary)]" />
    </header>

    <div v-if="orderedChallenges.length === 0" class="awdp-challenge-matrix__empty">
      No challenge telemetry yet.
    </div>

    <div v-else class="awdp-challenge-matrix__grid">
      <article v-for="challenge in orderedChallenges" :key="challenge.challengeId" class="awdp-challenge-matrix__cell">
        <div class="awdp-challenge-matrix__top">
          <strong>{{ challenge.challengeName }}</strong>
          <CommandSignal :label="challenge.category" :tone="categoryTone(challenge.category)" />
        </div>
        <div class="awdp-challenge-matrix__heat">
          <span>Attack pressure</span>
          <b>{{ challenge.attackHeat }}%</b>
          <i><em :style="{ width: `${Math.min(100, challenge.attackHeat)}%` }" /></i>
        </div>
        <div class="awdp-challenge-matrix__stats">
          <span>PASS <b>{{ challenge.defensePassedCount }}</b></span>
          <span>FAIL <b>{{ challenge.defenseFailedCount }}</b></span>
          <span>LIVE <b>{{ challenge.activeTeamCount }}/{{ challenge.instanceCount }}</b></span>
        </div>
      </article>
    </div>
  </CommandPanel>
</template>

<style scoped>
.awdp-challenge-matrix__header { display: flex; min-height: 68px; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--v2-line); padding: 12px 14px; }
.awdp-challenge-matrix__header h2 { margin: 5px 0 0; font-size: 16px; font-weight: 650; }
.awdp-challenge-matrix__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); }
.awdp-challenge-matrix__cell { min-width: 0; border-right: 1px solid rgb(26 58 103 / 0.7); border-bottom: 1px solid rgb(26 58 103 / 0.7); padding: 12px 14px; }
.awdp-challenge-matrix__cell:nth-child(2n) { border-right: 0; }
.awdp-challenge-matrix__top { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: 8px; }
.awdp-challenge-matrix__top strong { overflow: hidden; color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.awdp-challenge-matrix__heat { display: grid; grid-template-columns: 1fr auto; gap: 5px 8px; margin-top: 13px; color: var(--v2-text-muted); font-size: 10px; }
.awdp-challenge-matrix__heat b { color: var(--v2-warning); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
.awdp-challenge-matrix__heat i { grid-column: 1 / -1; display: block; height: 3px; overflow: hidden; background: var(--v2-surface-hover); }
.awdp-challenge-matrix__heat em { display: block; height: 100%; background: var(--v2-warning); box-shadow: 0 0 8px rgb(255 209 102 / 0.65); }
.awdp-challenge-matrix__stats { display: flex; flex-wrap: wrap; gap: 6px 11px; margin-top: 11px; color: var(--v2-text-faint); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 9px; }
.awdp-challenge-matrix__stats b { color: var(--v2-text); font-weight: 700; }
.awdp-challenge-matrix__empty { display: grid; min-height: 180px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 12px; }

@media (max-width: 600px) {
  .awdp-challenge-matrix__grid { grid-template-columns: 1fr; }
  .awdp-challenge-matrix__cell { border-right: 0; }
}
</style>
