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
.awdp-challenge-matrix__header { display: flex; min-height: 72px; align-items: center; justify-content: space-between; padding: 16px 18px 8px; }
.awdp-challenge-matrix__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.awdp-challenge-matrix__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; padding: 6px 18px 18px; }
.awdp-challenge-matrix__cell { min-width: 0; border-radius: 12px; padding: 14px; background: var(--v2-surface); box-shadow: var(--v2-inset); }
.awdp-challenge-matrix__top { display: flex; min-width: 0; align-items: center; justify-content: space-between; gap: 8px; }
.awdp-challenge-matrix__top strong { overflow: hidden; color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 12px; text-overflow: ellipsis; white-space: nowrap; }
.awdp-challenge-matrix__heat { display: grid; grid-template-columns: 1fr auto; gap: 6px 8px; margin-top: 13px; color: var(--v2-text-muted); font-size: 11px; }
.awdp-challenge-matrix__heat b { color: var(--v2-warning); font-family: var(--v2-font-mono); }
.awdp-challenge-matrix__heat i { grid-column: 1 / -1; display: block; height: 8px; overflow: hidden; border-radius: 999px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); }
.awdp-challenge-matrix__heat em { display: block; height: 100%; border-radius: 999px; background: var(--v2-warning); }
.awdp-challenge-matrix__stats { display: flex; flex-wrap: wrap; gap: 6px 12px; margin-top: 12px; color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 10px; }
.awdp-challenge-matrix__stats b { color: var(--v2-text); font-weight: 700; }
.awdp-challenge-matrix__empty { display: grid; min-height: 180px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 13px; }

@media (max-width: 600px) {
  .awdp-challenge-matrix__grid { grid-template-columns: 1fr; }
}
</style>
