<script setup lang="ts">
import type { AwdpScreenSnapshot } from '@/types/awdpScreen'
import { Activity, Gauge, ShieldCheck, ShieldX, Swords, Target, TrendingUp, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'

const props = defineProps<{
  stats: AwdpScreenSnapshot['stats']
  currentRound: number
}>()

const statItems = computed(() => [
  { label: 'Active teams', value: props.stats.activeTeamCount, sub: `${props.stats.teamCount} total`, icon: UsersRound, tone: 'text-slate-300' },
  { label: 'Active challenges', value: props.stats.activeChallengeCount, sub: `${props.stats.challengeCount} total`, icon: Target, tone: 'text-slate-300' },
  { label: 'Round', value: props.currentRound, sub: 'current', icon: Activity, tone: 'text-slate-300' },
  { label: 'Attack success', value: props.stats.attackSuccessCount, sub: `${props.stats.attackFailCount} failed`, icon: Swords, tone: 'text-emerald-200' },
  { label: 'Defense success', value: props.stats.defenseSuccessCount, sub: `${props.stats.defenseFailCount} failed`, icon: ShieldCheck, tone: 'text-emerald-200' },
  { label: 'Attack failed', value: props.stats.attackFailCount, sub: 'attempt results', icon: Gauge, tone: 'text-amber-200' },
  { label: 'Defense failed', value: props.stats.defenseFailCount, sub: 'rule dependent', icon: ShieldX, tone: 'text-amber-200' },
  { label: 'Round score', value: props.stats.totalScoreDelta ?? 0, sub: 'settled total', icon: TrendingUp, tone: 'text-slate-300', signed: true },
])

function formatNumber(value: number, signed?: boolean) {
  const prefix = signed && value > 0 ? '+' : ''
  return `${prefix}${new Intl.NumberFormat().format(value)}`
}
</script>

<template>
  <section class="grid grid-cols-2 gap-2 md:grid-cols-4 xl:grid-cols-8">
    <div
      v-for="item in statItems"
      :key="item.label"
      class="awdp-panel min-h-[4.1rem] p-2.5"
    >
      <div class="flex items-center justify-between gap-2">
        <span class="text-[11px] font-semibold text-slate-400">{{ item.label }}</span>
        <component :is="item.icon" class="size-4" :class="item.tone" />
      </div>
      <div class="mt-1.5 font-mono text-xl font-semibold tabular-nums text-slate-50">
        {{ formatNumber(item.value, item.signed) }}
      </div>
      <div class="mt-0.5 truncate text-[10px] text-slate-500">
        {{ item.sub }}
      </div>
    </div>
  </section>
</template>
