<script setup lang="ts">
import { ArrowDownRight, ArrowUpRight, Minus, Trophy } from 'lucide-vue-next'
import CommandPanel from '../primitives/CommandPanel.vue'
import type { CommandTeam } from '../mock/shared-theme-mock'

defineProps<{
  teams: CommandTeam[]
}>()
</script>

<template>
  <CommandPanel class="rank-table">
    <header class="rank-table__header">
      <div>
        <span>Live standings</span>
        <h2>Team ranking</h2>
      </div>
      <Trophy class="size-4 text-[var(--v2-warning)]" />
    </header>
    <div class="rank-table__labels">
      <span>Rank</span>
      <span>Team</span>
      <span>Attack</span>
      <span>Defense</span>
      <span>Score</span>
    </div>
    <div class="rank-table__rows">
      <article v-for="team in teams" :key="team.name" class="rank-table__row">
        <strong :class="{ 'rank-table__rank--leader': team.rank === 1 }">{{ String(team.rank).padStart(2, '0') }}</strong>
        <div class="rank-table__team">
          <span>{{ team.name }}</span>
          <ArrowUpRight v-if="team.trend === 'up'" class="size-3.5 text-[var(--v2-cyan)]" />
          <ArrowDownRight v-else-if="team.trend === 'down'" class="size-3.5 text-[var(--v2-danger)]" />
          <Minus v-else class="size-3.5 text-[var(--v2-text-faint)]" />
        </div>
        <span>{{ team.attack }}</span>
        <span>{{ team.defense }}</span>
        <b>{{ team.score.toLocaleString() }}</b>
      </article>
    </div>
  </CommandPanel>
</template>

<style scoped>
.rank-table__header {
  display: flex;
  min-height: 76px;
  align-items: center;
  justify-content: space-between;
  padding: 16px 18px 10px;
}

.rank-table__header span,
.rank-table__labels {
  color: var(--v2-text-muted);
  font-size: 10px;
  font-weight: 600;
  letter-spacing: 0.04em;
}

.rank-table__header h2 { margin: 7px 0 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }

.rank-table__labels,
.rank-table__row {
  display: grid;
  grid-template-columns: 46px minmax(120px, 1fr) 74px 74px 82px;
  align-items: center;
  gap: 10px;
}

.rank-table__labels { min-height: 34px; padding: 0 18px; }
.rank-table__labels span:not(:nth-child(2)) { text-align: right; }
.rank-table__rows { padding: 0 18px 12px; }
.rank-table__row { min-height: 48px; box-shadow: inset 0 -2px 0 rgb(184 188 194 / 0.5), inset 0 -1px 0 rgb(255 255 255 / 0.85); color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 12px; }
.rank-table__row:last-child { box-shadow: none; }
.rank-table__row > *:not(:nth-child(2)) { text-align: right; }
.rank-table__row > strong { color: var(--v2-text-faint); font-weight: 600; }
.rank-table__rank--leader { color: var(--v2-warning) !important; }
.rank-table__team { display: flex; align-items: center; gap: 6px; min-width: 0; color: var(--v2-text); font-family: var(--v2-font-sans); font-weight: 600; }
.rank-table__team span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.rank-table__row b { color: var(--v2-cyan); font-weight: 700; }

@media (max-width: 640px) {
  .rank-table__labels,
  .rank-table__row { grid-template-columns: 38px minmax(100px, 1fr) 68px 82px; }
  .rank-table__labels span:nth-child(3),
  .rank-table__row > span:nth-of-type(1) { display: none; }
}
</style>
