<script setup lang="ts">
import type { AwdpTeamScore, AwdpTeamTrend } from '@/types/awdpScreen'
import { ArrowDown, ArrowUp, Circle, Minus } from 'lucide-vue-next'
import { computed } from 'vue'

interface Props {
  team: AwdpTeamScore
  maxAttackScore: number
  maxDefenseScore: number
  maxTotalScore: number
}

const props = defineProps<Props>()

const rankTone = computed(() => {
  if (props.team.rank === 1)
    return 'gold'
  if (props.team.rank === 2)
    return 'silver'
  if (props.team.rank === 3)
    return 'bronze'
  return 'default'
})

const rankClass = computed(() => {
  if (rankTone.value === 'gold')
    return 'border-[var(--semantic-warning-border)] bg-[var(--semantic-warning-soft)] text-[var(--semantic-warning)]'
  if (rankTone.value === 'silver')
    return 'border-[var(--awdp-border)] bg-[var(--semantic-neutral-soft)] text-[var(--awdp-text)]'
  if (rankTone.value === 'bronze')
    return 'border-[var(--semantic-danger-border)] bg-[var(--semantic-danger-soft)] text-[var(--semantic-danger)]'
  return 'border-[var(--awdp-border)] bg-[var(--awdp-surface)] text-[var(--awdp-text)]'
})

const rowClass = computed(() => {
  if (rankTone.value === 'gold')
    return 'border-[var(--semantic-warning-border)] bg-[var(--semantic-warning-soft)]'
  if (rankTone.value === 'silver')
    return 'border-[var(--awdp-border)] bg-[var(--semantic-neutral-soft)]'
  if (rankTone.value === 'bronze')
    return 'border-[var(--semantic-danger-border)] bg-[var(--semantic-danger-soft)]'
  return 'border-[var(--awdp-border)] bg-[var(--awdp-surface)] hover:bg-[var(--awdp-surface-muted)]'
})

const attackWidth = computed(() => `${Math.min(100, (props.maxAttackScore > 0 ? (props.team.attackScore / props.maxAttackScore) : 0) * 100).toFixed(1)}%`)
const defenseWidth = computed(() => `${Math.min(100, (props.maxDefenseScore > 0 ? (props.team.defenseScore / props.maxDefenseScore) : 0) * 100).toFixed(1)}%`)

function trendIcon(trend: AwdpTeamTrend) {
  if (trend === 'up')
    return ArrowUp
  if (trend === 'down')
    return ArrowDown
  return Minus
}

function trendClass(trend: AwdpTeamTrend) {
  if (trend === 'up')
    return 'text-[var(--semantic-success)]'
  if (trend === 'down')
    return 'text-[var(--semantic-danger)]'
  return 'text-[var(--awdp-text-muted)]'
}

function isActive(lastActiveAt?: string) {
  return Boolean(lastActiveAt && Date.now() - Date.parse(lastActiveAt) < 5 * 60 * 1000)
}

function rankOffset(previousRank?: number, currentRank?: number) {
  if (previousRank === undefined || currentRank === undefined)
    return null
  return previousRank - currentRank
}

const offset = computed(() => rankOffset(props.team.previousRank, props.team.rank))
</script>

<template>
  <div
    class="score-row grid min-h-[3.6rem] grid-cols-[2.25rem_minmax(0,1fr)_5rem] items-center gap-2.5 rounded-lg border px-2.5 py-1.5 transition-colors"
    :class="rowClass"
  >
    <!-- rank -->
    <div class="flex flex-col items-center justify-center">
      <div
        class="flex size-7 items-center justify-center rounded-md border font-mono text-xs font-bold"
        :class="rankClass"
      >
        {{ team.rank }}
      </div>
      <span
        v-if="offset !== null"
        class="mt-0.5 text-[9px] font-bold"
        :class="offset > 0 ? 'text-[var(--semantic-success)]' : offset < 0 ? 'text-[var(--semantic-danger)]' : 'text-[var(--semantic-neutral)]'"
      >
        {{ offset > 0 ? `+${offset}` : offset < 0 ? `${offset}` : '=' }}
      </span>
    </div>

    <!-- team + bars -->
    <div class="min-w-0">
      <div class="flex items-center gap-2">
        <span class="truncate text-sm font-semibold text-[var(--awdp-text)]">{{ team.teamName }}</span>
        <Circle
          class="size-2 shrink-0"
          :class="isActive(team.lastActiveAt) ? 'fill-[var(--semantic-success)] text-[var(--semantic-success)]' : 'fill-[var(--semantic-neutral)] text-[var(--semantic-neutral)]'"
        />
      </div>
      <div class="mt-1.5 space-y-1">
        <div class="flex items-center gap-1.5">
          <span class="w-6 shrink-0 text-[9px] font-bold uppercase text-[var(--awdp-text-muted)]">Atk</span>
          <div class="h-1 flex-1 overflow-hidden rounded-full bg-[var(--awdp-surface-muted)]">
            <div
              class="h-full rounded-full bg-[var(--semantic-info)] transition-[width] duration-500"
              :style="{ width: attackWidth }"
            />
          </div>
        </div>
        <div class="flex items-center gap-1.5">
          <span class="w-6 shrink-0 text-[9px] font-bold uppercase text-[var(--awdp-text-muted)]">Def</span>
          <div class="h-1 flex-1 overflow-hidden rounded-full bg-[var(--awdp-surface-muted)]">
            <div
              class="h-full rounded-full bg-[var(--semantic-success)] transition-[width] duration-500"
              :style="{ width: defenseWidth }"
            />
          </div>
        </div>
      </div>
    </div>

    <!-- score + trend -->
    <div class="text-right">
      <div class="font-mono text-lg font-semibold tabular-nums text-[var(--awdp-text)]">
        {{ team.totalScore }}
      </div>
      <div class="mt-1 flex items-center justify-end gap-2 text-[11px]">
        <component :is="trendIcon(team.trend)" class="size-3.5" :class="trendClass(team.trend)" />
        <span class="font-mono text-[var(--awdp-text-muted)]">{{ team.currentRoundScore }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.score-row {
  transition:
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1),
    background-color 180ms cubic-bezier(0.16, 1, 0.3, 1),
    border-color 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

@media (prefers-reduced-motion: reduce) {
  .score-row {
    transition: none;
  }
}
</style>
