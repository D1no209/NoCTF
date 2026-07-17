<script setup lang="ts">
import type { AwdpRoundStat } from '@/types/awdpScreen'
import { computed } from 'vue'

interface Props {
  rounds: AwdpRoundStat[]
  currentRound: number
  maxRows?: number
}

const props = withDefaults(defineProps<Props>(), {
  maxRows: 12,
})

const ROW_H = 28
const PAD_Y = 6
const LABEL_W = 44
const COUNT_W = 40
const BAR_H = 14
const VIEW_W = 640

const visibleRounds = computed(() => props.rounds.slice(-props.maxRows))
const viewHeight = computed(() => visibleRounds.value.length * ROW_H + PAD_Y * 2)

const segments = [
  { key: 'attackSuccessCount', color: 'var(--semantic-info)', label: '命中' },
  { key: 'attackFailCount', color: 'var(--semantic-warning)', label: '被防' },
  { key: 'defenseSuccessCount', color: 'var(--semantic-success)', label: '防御' },
  { key: 'defenseFailCount', color: 'var(--semantic-danger)', label: '失守' },
] as const

const maxTotal = computed(() => Math.max(1, ...visibleRounds.value.map(round =>
  round.attackSuccessCount + round.attackFailCount + round.defenseSuccessCount + round.defenseFailCount,
)))

const rows = computed(() => visibleRounds.value.map((round, index) => {
  const y = PAD_Y + index * ROW_H
  const total = round.attackSuccessCount + round.attackFailCount + round.defenseSuccessCount + round.defenseFailCount
  let x = LABEL_W + 8
  const bars = segments.map((segment) => {
    const value = round[segment.key]
    const width = (value / maxTotal.value) * (VIEW_W - LABEL_W - COUNT_W - 24)
    const bar = {
      x,
      y: y + (ROW_H - BAR_H) / 2,
      width: Math.max(0, width),
      height: BAR_H,
      color: segment.color,
      value,
      label: segment.label,
    }
    x += bar.width
    return bar
  })

  return {
    round,
    y,
    total,
    bars,
    isCurrent: round.round === props.currentRound,
  }
}))
</script>

<template>
  <svg
    :viewBox="`0 0 ${VIEW_W} ${viewHeight}`"
    class="h-full w-full"
    preserveAspectRatio="xMidYMid meet"
    aria-hidden="true"
  >
    <g v-for="row in rows" :key="row.round.round">
      <!-- row background for current round -->
      <rect
        v-if="row.isCurrent"
        x="0"
        :y="row.y - 2"
        :width="VIEW_W"
        :height="ROW_H - 4"
        rx="6"
        fill="var(--semantic-info-soft)"
        stroke="var(--semantic-info-border)"
        stroke-width="1"
      />

      <text
        :x="LABEL_W"
        :y="row.y + ROW_H / 2 + 4"
        text-anchor="end"
        class="fill-[var(--awdp-text)] text-[11px] font-bold"
      >
        R{{ row.round.round }}
      </text>

      <!-- stacked bar -->
      <rect
        v-for="(bar, index) in row.bars"
        :key="index"
        :x="bar.x"
        :y="bar.y"
        :width="bar.width"
        :height="bar.height"
        rx="3"
        :fill="bar.color"
        opacity="0.92"
      >
        <title>{{ bar.label }}: {{ bar.value }}</title>
      </rect>

      <!-- total count -->
      <text
        :x="VIEW_W - 6"
        :y="row.y + ROW_H / 2 + 4"
        text-anchor="end"
        class="fill-[var(--awdp-text-muted)] text-[10px] font-mono"
      >
        {{ row.total }}
      </text>
    </g>
  </svg>
</template>
