<script setup lang="ts">
import { AlertTriangle, Trophy } from 'lucide-vue-next'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandLeaderboardEntry {
  rank?: number
  teamId?: string | null
  teamName?: string | null
  totalScore?: number | null
  score?: number | null
  solvedCount?: number | null
}

const props = defineProps<{
  entries: CommandLeaderboardEntry[]
  loading: boolean
  error?: boolean
}>()

function score(entry: CommandLeaderboardEntry) {
  return entry.totalScore ?? entry.score ?? 0
}
</script>

<template>
  <CommandPanel class="leaderboard-table">
    <header class="leaderboard-table__header">
      <div>
        <CommandSignal label="Scoreboard service" tone="warning" />
        <h2>Top teams</h2>
      </div>
      <Trophy class="size-4 text-[var(--v2-warning)]" />
    </header>

    <div class="leaderboard-table__labels">
      <span>Rank</span>
      <span>Team</span>
      <span>Solves</span>
      <span>Score</span>
    </div>

    <div v-if="props.loading" class="leaderboard-table__loading">Synchronizing leaderboard data...</div>
    <div v-else-if="props.error" class="leaderboard-table__loading leaderboard-table__loading--error">
      <AlertTriangle class="size-4" />
      <span>Leaderboard data could not be synchronized.</span>
    </div>
    <div v-else-if="props.entries.length" class="leaderboard-table__rows">
      <div v-for="(entry, index) in props.entries.slice(0, 8)" :key="entry.teamId || `${entry.teamName}-${index}`" class="leaderboard-table__row">
        <strong :class="{ 'leaderboard-table__leader': (entry.rank ?? index + 1) === 1 }">{{ String(entry.rank ?? index + 1).padStart(2, '0') }}</strong>
        <span>{{ entry.teamName || 'Unidentified team' }}</span>
        <span>{{ entry.solvedCount ?? 0 }}</span>
        <b>{{ score(entry).toLocaleString() }}</b>
      </div>
    </div>
    <div v-else class="leaderboard-table__loading">No leaderboard rows returned.</div>
  </CommandPanel>
</template>

<style scoped>
.leaderboard-table { display: grid; min-height: 0; align-content: start; }
.leaderboard-table__header { display: flex; min-height: 72px; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--v2-line); padding: 14px 16px; }
.leaderboard-table__header h2 { margin: 5px 0 0; color: var(--v2-text); font-size: 16px; font-weight: 650; }
.leaderboard-table__labels, .leaderboard-table__row { display: grid; grid-template-columns: 42px minmax(110px, 1fr) 54px 76px; align-items: center; gap: 8px; }
.leaderboard-table__labels { min-height: 34px; border-bottom: 1px solid rgb(26 58 103 / 0.72); padding: 0 16px; color: var(--v2-text-faint); font-size: 9px; font-weight: 700; letter-spacing: 0.08em; text-transform: uppercase; }
.leaderboard-table__labels > :not(:nth-child(2)), .leaderboard-table__row > :not(:nth-child(2)) { text-align: right; }
.leaderboard-table__rows { padding: 0 16px; }
.leaderboard-table__row { min-height: 42px; border-bottom: 1px solid rgb(26 58 103 / 0.6); color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px; }
.leaderboard-table__row:last-child { border-bottom: 0; }
.leaderboard-table__row strong { color: var(--v2-text-faint); font-weight: 600; }
.leaderboard-table__row span:nth-child(2) { overflow: hidden; color: var(--v2-text); font-family: Inter, ui-sans-serif, system-ui, sans-serif; font-weight: 650; text-overflow: ellipsis; white-space: nowrap; }
.leaderboard-table__row b { color: var(--v2-cyan); font-weight: 700; }
.leaderboard-table__leader { color: var(--v2-warning) !important; }
.leaderboard-table__loading { padding: 24px 16px; color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px; text-align: center; }
.leaderboard-table__loading--error { display: flex; align-items: center; justify-content: center; gap: 7px; color: var(--v2-danger); }
</style>
