<script setup lang="ts">
import type { AwdpScreenSnapshot } from '@/types/awdpScreen'
import { Activity, Gauge, ShieldCheck, ShieldX, Swords, Target, TrendingUp, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Panel } from '@/ui-v1/components/ui/panel'

const props = defineProps<{
  stats: AwdpScreenSnapshot['stats']
  currentRound: number
}>()

const { t } = useI18n()

const statItems = computed(() => [
  { label: t('awdpScreen.stats.activeTeams'), value: props.stats.activeTeamCount, sub: t('awdpScreen.stats.total', { count: props.stats.teamCount }), icon: UsersRound, tone: 'text-[var(--semantic-info)]' },
  { label: t('awdpScreen.stats.activeChallenges'), value: props.stats.activeChallengeCount, sub: t('awdpScreen.stats.total', { count: props.stats.challengeCount }), icon: Target, tone: 'text-[var(--semantic-info)]' },
  { label: t('awdpScreen.stats.round'), value: props.currentRound, sub: t('awdpScreen.stats.current'), icon: Activity, tone: 'text-[var(--semantic-warning)]' },
  { label: t('awdpScreen.stats.attackSuccess'), value: props.stats.attackSuccessCount, sub: t('awdpScreen.stats.failed', { count: props.stats.attackFailCount }), icon: Swords, tone: 'text-[var(--semantic-success)]' },
  { label: t('awdpScreen.stats.defenseSuccess'), value: props.stats.defenseSuccessCount, sub: t('awdpScreen.stats.failed', { count: props.stats.defenseFailCount }), icon: ShieldCheck, tone: 'text-[var(--semantic-success)]' },
  { label: t('awdpScreen.stats.attackFailed'), value: props.stats.attackFailCount, sub: t('awdpScreen.stats.attemptResults'), icon: Gauge, tone: 'text-[var(--semantic-warning)]' },
  { label: t('awdpScreen.stats.defenseFailed'), value: props.stats.defenseFailCount, sub: t('awdpScreen.stats.ruleDependent'), icon: ShieldX, tone: 'text-[var(--semantic-warning)]' },
  { label: t('awdpScreen.stats.scoreDelta'), value: props.stats.totalScoreDelta ?? 0, sub: t('awdpScreen.stats.attackDefense'), icon: TrendingUp, tone: 'text-[var(--semantic-info)]', signed: true },
])

function formatNumber(value: number, signed?: boolean) {
  const prefix = signed && value > 0 ? '+' : ''
  return `${prefix}${new Intl.NumberFormat().format(value)}`
}
</script>

<template>
  <section class="grid grid-cols-2 gap-3 md:grid-cols-4 xl:grid-cols-8">
    <Panel
      v-for="item in statItems"
      :key="item.label"
      variant="default"
      class="min-h-[4.35rem] p-2.5"
    >
      <div class="flex items-center justify-between gap-2">
        <span class="text-[11px] font-semibold uppercase text-[var(--awdp-text-muted)]">{{ item.label }}</span>
        <component :is="item.icon" class="size-4" :class="item.tone" />
      </div>
      <div class="mt-1.5 font-mono text-xl font-semibold tabular-nums text-[var(--awdp-text)]">
        {{ formatNumber(item.value, item.signed) }}
      </div>
      <div class="mt-0.5 truncate text-[10px] text-[var(--awdp-text-muted)]">
        {{ item.sub }}
      </div>
    </Panel>
  </section>
</template>
