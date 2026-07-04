<script setup lang="ts">
import type { AwdpTeamScore } from '@/types/awdpScreen'
import { ArrowDown, ArrowUp, Circle, Minus, RotateCw, Trophy } from 'lucide-vue-next'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'

const props = defineProps<{
  teams: AwdpTeamScore[]
}>()

const PAGE_SIZE = 8
const ROTATE_MS = 5_800
const page = ref(0)
let rotateTimer: ReturnType<typeof setInterval> | null = null

const pageCount = computed(() => Math.max(1, Math.ceil(props.teams.length / PAGE_SIZE)))
const visibleTeams = computed(() => props.teams.slice(page.value * PAGE_SIZE, page.value * PAGE_SIZE + PAGE_SIZE))
const pageLabel = computed(() => `${page.value + 1}/${pageCount.value}`)

watch(pageCount, (count) => {
  page.value = Math.min(page.value, count - 1)
})

onMounted(() => {
  rotateTimer = setInterval(() => {
    if (pageCount.value > 1)
      page.value = (page.value + 1) % pageCount.value
  }, ROTATE_MS)
})

onUnmounted(() => {
  if (rotateTimer)
    clearInterval(rotateTimer)
})

function trendIcon(trend: AwdpTeamScore['trend']) {
  if (trend === 'up')
    return ArrowUp
  if (trend === 'down')
    return ArrowDown
  return Minus
}

function trendClass(trend: AwdpTeamScore['trend']) {
  if (trend === 'up')
    return 'text-emerald-200'
  if (trend === 'down')
    return 'text-rose-200'
  return 'text-slate-400'
}

function isActive(lastActiveAt?: string) {
  return Boolean(lastActiveAt && Date.now() - Date.parse(lastActiveAt) < 5 * 60 * 1000)
}

function formatTime(value?: string) {
  if (!value)
    return 'idle'
  return new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(value))
}
</script>

<template>
  <section class="awdp-panel flex min-h-0 flex-col">
    <div class="flex items-center justify-between border-b border-slate-200/10 px-4 py-3">
      <div>
        <h2 class="text-sm font-semibold uppercase text-slate-100">
          Realtime scoreboard
        </h2>
        <p class="text-xs text-slate-500">
          Total score is attack, defense, and rule bonuses
        </p>
      </div>
      <div class="flex items-center gap-2 text-[11px] font-semibold uppercase text-slate-500">
        <RotateCw v-if="pageCount > 1" class="size-3.5 text-cyan-200" />
        <span>{{ pageLabel }}</span>
        <Trophy class="size-5 text-orange-100" />
      </div>
    </div>

    <div v-if="visibleTeams.length === 0" class="flex flex-1 items-center justify-center px-6 text-center text-sm text-slate-500">
      No teams are available for this screen.
    </div>

    <TransitionGroup
      v-else
      name="score-row"
      tag="div"
      class="min-h-0 flex-1 px-2.5 py-2.5"
    >
      <div
        v-for="team in visibleTeams"
        :key="team.teamId"
        class="score-row mb-1.5 grid min-h-[3.55rem] grid-cols-[2.35rem_minmax(0,1fr)_4.8rem] items-center gap-2 rounded-lg border px-2.5 py-1.5"
        :class="team.rank <= 3 ? 'border-orange-200/30 bg-orange-200/[0.06]' : 'border-slate-300/10 bg-slate-900/42'"
      >
        <div class="flex items-center gap-2">
          <div
            class="flex size-7 items-center justify-center rounded-md border font-mono text-xs font-semibold"
            :class="team.rank <= 3 ? 'border-orange-200/35 text-orange-100' : 'border-slate-300/15 text-slate-200'"
          >
            {{ team.rank }}
          </div>
        </div>

        <div class="min-w-0">
          <div class="flex items-center gap-2">
            <span class="truncate text-sm font-semibold text-slate-100">{{ team.teamName }}</span>
            <Circle
              class="size-2.5 shrink-0"
              :class="isActive(team.lastActiveAt) ? 'fill-emerald-300 text-emerald-300' : 'fill-slate-600 text-slate-600'"
            />
          </div>
          <div class="mt-0.5 grid grid-cols-3 gap-1 text-[10px] text-slate-500">
            <span>A {{ team.attackScore }}</span>
            <span>D {{ team.defenseScore }}</span>
            <span>R {{ team.currentRoundScore }}</span>
          </div>
        </div>

        <div class="text-right">
          <div class="font-mono text-lg font-semibold tabular-nums text-slate-50">
            {{ team.totalScore }}
          </div>
          <div class="mt-1 flex items-center justify-end gap-2 text-[11px]">
            <component :is="trendIcon(team.trend)" class="size-3.5" :class="trendClass(team.trend)" />
            <span class="font-mono text-slate-500">{{ formatTime(team.lastActiveAt) }}</span>
          </div>
        </div>
      </div>
    </TransitionGroup>
  </section>
</template>

<style scoped>
.score-row {
  transition:
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1),
    background-color 180ms cubic-bezier(0.16, 1, 0.3, 1),
    border-color 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.score-row-move,
.score-row-enter-active,
.score-row-leave-active {
  transition:
    opacity 180ms cubic-bezier(0.16, 1, 0.3, 1),
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.score-row-enter-from,
.score-row-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
