<script setup lang="ts">
import { RefreshCw, Trophy } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

export interface CommandKohHistoryEntry {
  teamId: string
  teamName: string
  startTime: string
  endTime: string | null
}

export interface CommandKohHill {
  challengeId: string
  challengeName: string
  controllerTeamId: string | null
  controllerTeamName: string | null
  controlDurationSeconds: number
  history: CommandKohHistoryEntry[]
}

const props = defineProps<{
  state: 'invalid' | 'loading' | 'error' | 'ready'
  errorMessage?: string
  hills: CommandKohHill[]
  signalConnected: boolean
}>()

const emit = defineEmits<{
  retry: []
}>()

const SEGMENT_TONES = ['var(--v2-info)', 'var(--v2-magenta)', 'var(--v2-cyan)', 'var(--v2-warning)']

function formatDuration(totalSeconds: number) {
  const seconds = Math.max(0, Math.floor(totalSeconds))
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

function formatClock(value: string | null | undefined) {
  if (!value)
    return 'now'
  const timestamp = Date.parse(value)
  if (!Number.isFinite(timestamp))
    return '--:--'
  return new Date(timestamp).toLocaleTimeString('en-GB', { hour12: false, hour: '2-digit', minute: '2-digit' })
}

function entrySeconds(entry: CommandKohHistoryEntry) {
  const start = Date.parse(entry.startTime)
  const end = entry.endTime ? Date.parse(entry.endTime) : Date.now()
  if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start)
    return 0
  return (end - start) / 1000
}

interface HillSegment {
  key: string
  teamName: string
  percent: number
  color: string
}

function hillSegments(hill: CommandKohHill): HillSegment[] {
  const totals = new Map<string, number>()
  for (const entry of hill.history)
    totals.set(entry.teamName, (totals.get(entry.teamName) ?? 0) + entrySeconds(entry))
  const total = [...totals.values()].reduce((sum, value) => sum + value, 0)
  if (total <= 0)
    return []

  const ordered = [...totals.entries()].sort((a, b) => b[1] - a[1])
  const colorOf = (teamName: string) => teamName === hill.controllerTeamName
    ? 'var(--v2-primary)'
    : SEGMENT_TONES[ordered.findIndex(([name]) => name === teamName) % SEGMENT_TONES.length]

  return ordered.map(([teamName, value]) => ({
    key: teamName,
    teamName,
    percent: Math.max(2, (value / total) * 100),
    color: colorOf(teamName),
  }))
}

function recentHistory(hill: CommandKohHill) {
  return [...hill.history].reverse().slice(0, 4)
}

const controlledCount = computed(() => props.hills.filter(hill => hill.controllerTeamId).length)
</script>

<template>
  <section v-if="props.state === 'loading'" class="koh-dashboard__state">
    <CommandPanel class="koh-dashboard__skeleton" aria-busy="true"><span /><span /><span /></CommandPanel>
  </section>

  <section v-else-if="props.state === 'invalid' || props.state === 'error'" class="koh-dashboard__state">
    <CommandPanel class="koh-dashboard__error" tone="warning">
      <div>
        <CommandSignal :label="props.state === 'invalid' ? 'Invalid competition identifier' : 'Dashboard unavailable'" tone="danger" />
        <h1>Unable to load the KoH workspace</h1>
        <p>{{ props.errorMessage || 'The dashboard service did not return usable hill data.' }}</p>
      </div>
      <CommandButton v-if="props.state === 'error'" label="Retry" @click="emit('retry')">
        <template #icon><RefreshCw class="size-4" /></template>
      </CommandButton>
    </CommandPanel>
  </section>

  <section v-else class="koh-dashboard">
    <header class="koh-dashboard__heading">
      <div>
        <CommandSignal label="KoH participant operations" tone="info" />
        <h1>Hill control room</h1>
      </div>
      <div class="koh-dashboard__meta">
        <CommandSignal :label="`${controlledCount}/${props.hills.length} hills held`" tone="success" />
        <CommandSignal :label="props.signalConnected ? 'Live SignalR' : 'SignalR reconnecting'" :tone="props.signalConnected ? 'success' : 'warning'" />
      </div>
    </header>

    <CommandPanel v-if="!props.hills.length" class="koh-dashboard__empty">
      <CommandSignal label="No hills published" tone="info" />
      <h2>No King of the Hill challenges are active</h2>
      <p>Hills appear here as soon as the organizer publishes them for this competition.</p>
    </CommandPanel>

    <div v-else class="koh-dashboard__grid">
      <CommandPanel v-for="hill in props.hills" :key="hill.challengeId" class="koh-hill">
        <header class="koh-hill__header">
          <h2>{{ hill.challengeName }}</h2>
          <CommandSignal
            :label="hill.controllerTeamName ? 'Controlled' : 'Open hill'"
            :tone="hill.controllerTeamName ? 'success' : 'warning'"
          />
        </header>

        <div class="koh-hill__controller">
          <span class="koh-hill__trophy">
            <Trophy class="size-4" />
          </span>
          <div>
            <strong>{{ hill.controllerTeamName ?? 'Awaiting controller' }}</strong>
            <span>{{ hill.controllerTeamName ? `holding ${formatDuration(hill.controlDurationSeconds)}` : 'no team holds this hill' }}</span>
          </div>
        </div>

        <div v-if="hillSegments(hill).length" class="koh-hill__share">
          <i
            v-for="segment in hillSegments(hill)"
            :key="segment.key"
            :title="segment.teamName"
            :style="{ width: `${segment.percent}%`, background: segment.color }"
          />
        </div>

        <ul v-if="recentHistory(hill).length" class="koh-hill__history">
          <li v-for="(entry, index) in recentHistory(hill)" :key="`${entry.teamId}-${entry.startTime}-${index}`">
            <span>{{ entry.teamName }}</span>
            <span>{{ formatClock(entry.startTime) }} → {{ entry.endTime ? formatClock(entry.endTime) : 'holding' }}</span>
          </li>
        </ul>
      </CommandPanel>
    </div>
  </section>
</template>

<style scoped>
.koh-dashboard,
.koh-dashboard__state { display: grid; gap: 16px; }

.koh-dashboard__heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 20px; padding: 4px 2px 0; }
.koh-dashboard__heading h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 24px; font-weight: 600; letter-spacing: -0.01em; }
.koh-dashboard__meta { display: flex; flex: none; align-items: center; gap: 10px; }

.koh-dashboard__skeleton { display: grid; min-height: 240px; grid-template-rows: 18px 1fr 1fr; gap: 14px; padding: 22px; }
.koh-dashboard__skeleton span { display: block; border-radius: 12px; background: var(--v2-surface-strong); box-shadow: var(--v2-inset); animation: koh-pulse 1.1s ease-in-out infinite alternate; }
.koh-dashboard__skeleton span:nth-child(1) { width: 24%; }

.koh-dashboard__error { display: flex; min-height: 210px; align-items: center; justify-content: space-between; gap: 20px; padding: 26px; }
.koh-dashboard__error h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 19px; font-weight: 600; }
.koh-dashboard__error p { max-width: 650px; margin: 8px 0 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.6; }

.koh-dashboard__empty { display: grid; min-height: 200px; align-content: center; justify-items: start; gap: 10px; padding: 26px; }
.koh-dashboard__empty h2 { margin: 0; color: var(--v2-text); font-size: 17px; font-weight: 600; }
.koh-dashboard__empty p { margin: 0; color: var(--v2-text-muted); font-size: 13px; }

.koh-dashboard__grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 16px; }

.koh-hill { display: grid; align-content: start; gap: 14px; padding: 16px; }
.koh-hill__header { display: flex; align-items: center; justify-content: space-between; gap: 10px; }
.koh-hill__header h2 { overflow: hidden; margin: 0; color: var(--v2-text); font-size: 16px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }

.koh-hill__controller {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  align-items: center;
  gap: 11px;
  border-radius: 12px;
  padding: 11px 13px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
}
.koh-hill__trophy {
  display: grid;
  width: 34px;
  height: 34px;
  place-items: center;
  border-radius: 999px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-warning);
}
.koh-hill__controller > div { display: grid; min-width: 0; gap: 3px; }
.koh-hill__controller strong { overflow: hidden; color: var(--v2-text); font-size: 13px; font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.koh-hill__controller span { color: var(--v2-text-muted); font-family: var(--v2-font-mono); font-size: 11px; }

.koh-hill__share {
  display: flex;
  height: 10px;
  overflow: hidden;
  gap: 3px;
  border-radius: 999px;
  padding: 2px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
}
.koh-hill__share i { display: block; border-radius: 999px; }

.koh-hill__history { display: grid; gap: 6px; margin: 0; padding: 0; list-style: none; }
.koh-hill__history li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  border-radius: 10px;
  padding: 8px 11px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-text-muted);
  font-size: 11px;
}
.koh-hill__history li span:first-child { overflow: hidden; color: var(--v2-text); font-weight: 600; text-overflow: ellipsis; white-space: nowrap; }
.koh-hill__history li span:last-child { flex: none; font-family: var(--v2-font-mono); }

@keyframes koh-pulse {
  to { opacity: 0.45; }
}

@media (max-width: 700px) {
  .koh-dashboard__heading { align-items: flex-start; flex-direction: column; }
}
</style>
