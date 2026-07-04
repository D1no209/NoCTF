<script setup lang="ts">
import type { AwdpScreenSnapshot } from '@/types/awdpScreen'
import { Activity, Gauge, ShieldCheck, ShieldX, Swords, Target, TrendingUp, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'

const props = defineProps<{
  stats: AwdpScreenSnapshot['stats']
  currentRound: number
}>()

const statItems = computed(() => [
  { label: 'Active teams', value: props.stats.activeTeamCount, sub: `${props.stats.teamCount} total`, icon: UsersRound, tone: 'neutral' },
  { label: 'Active services', value: props.stats.activeChallengeCount, sub: `${props.stats.challengeCount} challenges`, icon: Target, tone: 'neutral' },
  { label: 'Current round', value: props.currentRound, sub: 'live index', icon: Activity, tone: 'neutral' },
  { label: 'Break success', value: props.stats.attackSuccessCount, sub: `${props.stats.attackFailCount} failed`, icon: Swords, tone: 'break' },
  { label: 'Fix success', value: props.stats.defenseSuccessCount, sub: `${props.stats.defenseFailCount} failed`, icon: ShieldCheck, tone: 'fix' },
  { label: 'Break failed', value: props.stats.attackFailCount, sub: 'attempt result', icon: Gauge, tone: 'warn' },
  { label: 'Fix failed', value: props.stats.defenseFailCount, sub: 'rule state', icon: ShieldX, tone: 'warn' },
  { label: 'Settled score', value: props.stats.totalScoreDelta ?? 0, sub: 'round ledger', icon: TrendingUp, tone: 'neutral', signed: true },
])

function formatNumber(value: number, signed?: boolean) {
  const prefix = signed && value > 0 ? '+' : ''
  return `${prefix}${new Intl.NumberFormat().format(value)}`
}
</script>

<template>
  <section class="awdp-signal-strip">
    <div
      v-for="item in statItems"
      :key="item.label"
      class="signal-cell"
      :data-tone="item.tone"
    >
      <div class="signal-cell-top">
        <span>{{ item.label }}</span>
        <component :is="item.icon" class="size-4" />
      </div>
      <div class="signal-value">
        {{ formatNumber(item.value, item.signed) }}
      </div>
      <div class="signal-sub">
        {{ item.sub }}
      </div>
    </div>
  </section>
</template>

<style scoped>
.awdp-signal-strip {
  display: grid;
  grid-template-columns: repeat(8, minmax(0, 1fr));
  gap: clamp(0.36rem, 0.52vw, 0.62rem);
}

.signal-cell {
  min-height: 4.15rem;
  border: 1px solid var(--awdp-screen-border);
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--awdp-screen-panel-raised) 74%, var(--awdp-screen-bg));
  padding: 0.62rem 0.68rem;
}

.signal-cell-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.55rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 58%, transparent);
  font-size: 0.66rem;
  font-weight: 800;
  text-transform: uppercase;
}

.signal-cell[data-tone="break"] .signal-cell-top {
  color: var(--awdp-break);
}

.signal-cell[data-tone="fix"] .signal-cell-top {
  color: var(--awdp-fix);
}

.signal-cell[data-tone="warn"] .signal-cell-top {
  color: var(--awdp-warn);
}

.signal-value {
  margin-top: 0.35rem;
  color: var(--sidebar-foreground);
  font-size: clamp(1rem, 1.25vw, 1.45rem);
  font-weight: 800;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}

.signal-sub {
  margin-top: 0.22rem;
  overflow: hidden;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.62rem;
  text-overflow: ellipsis;
  white-space: nowrap;
}

@media (max-width: 1280px) {
  .awdp-signal-strip {
    grid-template-columns: repeat(4, minmax(0, 1fr));
  }
}
</style>
