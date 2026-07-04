<script setup lang="ts">
import type { AwdpTeamScore } from '@/types/awdpScreen'
import { ArrowDown, ArrowUp, Circle, Minus, RotateCw } from 'lucide-vue-next'
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
    return 'trend-up'
  if (trend === 'down')
    return 'trend-down'
  return 'trend-stable'
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
  <section class="awdp-panel scoreboard-panel">
    <div class="scoreboard-head">
      <div>
        <h2>
          Scoreboard
        </h2>
        <p>
          Round-settled break and fix ledger
        </p>
      </div>
      <div class="scoreboard-page">
        <RotateCw v-if="pageCount > 1" class="size-3.5" />
        <span>{{ pageLabel }}</span>
      </div>
    </div>

    <div v-if="visibleTeams.length === 0" class="scoreboard-empty">
      No teams are available for this screen.
    </div>

    <TransitionGroup
      v-else
      name="score-row"
      tag="div"
      class="score-list"
    >
      <div
        v-for="team in visibleTeams"
        :key="team.teamId"
        class="score-row"
        :data-podium="team.rank <= 3"
      >
        <div class="score-rank">
          {{ team.rank }}
        </div>

        <div class="score-team">
          <div class="score-team-name">
            <span>{{ team.teamName }}</span>
            <Circle
              class="score-active-dot"
              :class="isActive(team.lastActiveAt) ? 'is-active' : ''"
            />
          </div>
          <div class="score-breakdown">
            <span>B {{ team.attackScore }}</span>
            <span>F {{ team.defenseScore }}</span>
            <span>R {{ team.currentRoundScore }}</span>
          </div>
        </div>

        <div class="score-total">
          <div>
            {{ team.totalScore }}
          </div>
          <div class="score-trend">
            <component :is="trendIcon(team.trend)" class="size-3.5" :class="trendClass(team.trend)" />
            <span>{{ formatTime(team.lastActiveAt) }}</span>
          </div>
        </div>
      </div>
    </TransitionGroup>
  </section>
</template>

<style scoped>
.scoreboard-panel {
  display: flex;
  min-height: 0;
  flex-direction: column;
  overflow: hidden;
}

.scoreboard-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  border-bottom: 1px solid var(--awdp-screen-border);
  padding: 0.78rem 0.9rem 0.68rem;
}

.scoreboard-head h2 {
  margin: 0;
  color: var(--sidebar-foreground);
  font-size: 0.82rem;
  font-weight: 850;
  text-transform: uppercase;
}

.scoreboard-head p {
  margin: 0.16rem 0 0;
  color: color-mix(in oklch, var(--sidebar-foreground) 44%, transparent);
  font-size: 0.68rem;
}

.scoreboard-page {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 48%, transparent);
  font-size: 0.68rem;
  font-weight: 800;
}

.scoreboard-empty {
  display: grid;
  flex: 1;
  place-items: center;
  padding: 1.5rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 45%, transparent);
  font-size: 0.82rem;
  text-align: center;
}

.score-list {
  min-height: 0;
  flex: 1;
  padding: 0.48rem;
}

.score-row {
  display: grid;
  grid-template-columns: 2.35rem minmax(0, 1fr) 5.2rem;
  align-items: center;
  gap: 0.55rem;
  min-height: calc((100% - 0.5rem * 7) / 8);
  margin-bottom: 0.5rem;
  border: 1px solid color-mix(in oklch, var(--sidebar-foreground) 10%, transparent);
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--awdp-screen-panel-raised) 48%, var(--awdp-screen-bg));
  padding: 0.42rem 0.52rem;
  transition:
    transform 180ms cubic-bezier(0.16, 1, 0.3, 1),
    background-color 180ms cubic-bezier(0.16, 1, 0.3, 1),
    border-color 180ms cubic-bezier(0.16, 1, 0.3, 1);
}

.score-row:last-child {
  margin-bottom: 0;
}

.score-row[data-podium="true"] {
  border-color: color-mix(in oklch, var(--awdp-warn) 30%, var(--awdp-screen-border));
  background: color-mix(in oklch, var(--awdp-warn) 10%, var(--awdp-screen-panel-raised));
}

.score-rank {
  display: grid;
  width: 1.95rem;
  height: 1.95rem;
  place-items: center;
  border: 1px solid color-mix(in oklch, var(--sidebar-foreground) 16%, transparent);
  border-radius: var(--radius-sm);
  color: var(--sidebar-foreground);
  font-size: 0.78rem;
  font-weight: 850;
  font-variant-numeric: tabular-nums;
}

.score-team {
  min-width: 0;
}

.score-team-name {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 0.45rem;
}

.score-team-name span {
  overflow: hidden;
  color: var(--sidebar-foreground);
  font-size: 0.8rem;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.score-active-dot {
  width: 0.5rem;
  height: 0.5rem;
  flex: 0 0 auto;
  fill: color-mix(in oklch, var(--sidebar-foreground) 26%, transparent);
  color: color-mix(in oklch, var(--sidebar-foreground) 26%, transparent);
}

.score-active-dot.is-active {
  fill: var(--awdp-fix);
  color: var(--awdp-fix);
}

.score-breakdown {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.25rem;
  margin-top: 0.18rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.62rem;
  font-variant-numeric: tabular-nums;
}

.score-total {
  text-align: right;
}

.score-total > div:first-child {
  color: var(--sidebar-foreground);
  font-size: 1.08rem;
  font-weight: 850;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}

.score-trend {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.28rem;
  margin-top: 0.28rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 42%, transparent);
  font-size: 0.62rem;
}

.trend-up {
  color: var(--awdp-fix);
}

.trend-down {
  color: var(--awdp-error);
}

.trend-stable {
  color: color-mix(in oklch, var(--sidebar-foreground) 46%, transparent);
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
