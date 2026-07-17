<script setup lang="ts">
import type { AwdpTeamScore } from '@/types/awdpScreen'
import { ArrowDownRight, ArrowUpRight, Minus, Trophy } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const props = defineProps<{
  teams: AwdpTeamScore[]
}>()

const visibleTeams = computed(() => props.teams.slice(0, 10))
const maxScore = computed(() => Math.max(1, ...props.teams.map(team => team.totalScore)))

function trendIcon(trend: AwdpTeamScore['trend']) {
  if (trend === 'up')
    return ArrowUpRight
  if (trend === 'down')
    return ArrowDownRight
  return Minus
}

function trendTone(trend: AwdpTeamScore['trend']) {
  if (trend === 'up')
    return 'success' as const
  if (trend === 'down')
    return 'danger' as const
  return 'info' as const
}
</script>

<template>
  <CommandPanel class="awdp-scoreboard">
    <header class="awdp-scoreboard__header">
      <div>
        <CommandSignal label="Live standings" tone="success" />
        <h2>Team ranking</h2>
      </div>
      <Trophy class="size-4 text-[var(--v2-warning)]" />
    </header>

    <div class="awdp-scoreboard__labels">
      <span>Rank</span>
      <span>Team</span>
      <span>Attack</span>
      <span>Defense</span>
      <span>Score</span>
    </div>

    <div v-if="visibleTeams.length === 0" class="awdp-scoreboard__empty">
      No team telemetry yet.
    </div>

    <div v-else class="awdp-scoreboard__rows">
      <article v-for="team in visibleTeams" :key="team.teamId" class="awdp-scoreboard__row">
        <strong :class="{ 'awdp-scoreboard__leader': team.rank === 1 }">{{ String(team.rank).padStart(2, '0') }}</strong>
        <div class="awdp-scoreboard__team">
          <span>{{ team.teamName }}</span>
          <component :is="trendIcon(team.trend)" class="size-3.5" :class="`awdp-scoreboard__trend--${trendTone(team.trend)}`" />
          <i :class="{ 'awdp-scoreboard__active': Boolean(team.lastActiveAt) }" aria-label="activity status" />
        </div>
        <span>{{ team.attackScore }}</span>
        <span>{{ team.defenseScore }}</span>
        <div class="awdp-scoreboard__score">
          <b>{{ team.totalScore.toLocaleString() }}</b>
          <i><span :style="{ width: `${(team.totalScore / maxScore) * 100}%` }" /></i>
        </div>
      </article>
    </div>
  </CommandPanel>
</template>

<style scoped>
.awdp-scoreboard { display: flex; min-height: 0; flex-direction: column; }
.awdp-scoreboard__header { display: flex; min-height: 68px; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--v2-line); padding: 12px 14px; }
.awdp-scoreboard__header h2 { margin: 5px 0 0; font-size: 16px; font-weight: 650; }
.awdp-scoreboard__labels,
.awdp-scoreboard__row { display: grid; grid-template-columns: 42px minmax(112px, 1fr) 58px 58px 76px; align-items: center; gap: 8px; }
.awdp-scoreboard__labels { min-height: 32px; border-bottom: 1px solid rgb(26 58 103 / 0.72); padding: 0 14px; color: var(--v2-text-faint); font-size: 9px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }
.awdp-scoreboard__labels span:not(:nth-child(2)) { text-align: right; }
.awdp-scoreboard__rows { padding: 0 14px; }
.awdp-scoreboard__row { min-height: 48px; border-bottom: 1px solid rgb(26 58 103 / 0.62); color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px; }
.awdp-scoreboard__row:last-child { border-bottom: 0; }
.awdp-scoreboard__row > *:not(:nth-child(2)) { text-align: right; }
.awdp-scoreboard__row > strong { color: var(--v2-text-faint); font-weight: 600; }
.awdp-scoreboard__leader { color: var(--v2-warning) !important; }
.awdp-scoreboard__team { display: flex; min-width: 0; align-items: center; gap: 5px; color: var(--v2-text); font-family: Inter, ui-sans-serif, system-ui, sans-serif; font-weight: 650; }
.awdp-scoreboard__team span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.awdp-scoreboard__team i { width: 5px; height: 5px; flex: none; background: var(--v2-text-faint); }
.awdp-scoreboard__team .awdp-scoreboard__active { background: var(--v2-cyan); box-shadow: 0 0 8px var(--v2-cyan); }
.awdp-scoreboard__trend--success { color: var(--v2-cyan); }
.awdp-scoreboard__trend--danger { color: var(--v2-danger); }
.awdp-scoreboard__trend--info { color: var(--v2-text-faint); }
.awdp-scoreboard__score { display: grid; justify-items: end; gap: 4px; }
.awdp-scoreboard__score b { color: var(--v2-cyan); font-weight: 700; }
.awdp-scoreboard__score i { display: block; width: 100%; height: 2px; overflow: hidden; background: var(--v2-surface-hover); }
.awdp-scoreboard__score i span { display: block; height: 100%; background: var(--v2-cyan); }
.awdp-scoreboard__empty { display: grid; min-height: 180px; place-items: center; padding: 18px; color: var(--v2-text-muted); font-size: 12px; }

@media (max-width: 620px) {
  .awdp-scoreboard__labels,
  .awdp-scoreboard__row { grid-template-columns: 38px minmax(100px, 1fr) 62px 74px; }
  .awdp-scoreboard__labels span:nth-child(3),
  .awdp-scoreboard__row > span:nth-of-type(1) { display: none; }
}
</style>
