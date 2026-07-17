<script setup lang="ts">
import type {
  AwdpChallengeStatus,
  AwdpRoundStat,
  AwdpScreenConnectionStatus,
  AwdpScreenEvent,
  AwdpScreenSnapshot,
  AwdpTeamScore,
} from '@/types/awdpScreen'
import { Activity, Flag, Gauge, RefreshCw, ShieldCheck, Swords, UsersRound } from 'lucide-vue-next'
import { computed } from 'vue'
import CommandAwdpChallengeMatrix from './CommandAwdpChallengeMatrix.vue'
import CommandAwdpEventFeed from './CommandAwdpEventFeed.vue'
import CommandAwdpRoundTimeline from './CommandAwdpRoundTimeline.vue'
import CommandAwdpScoreboard from './CommandAwdpScoreboard.vue'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

type WorkspaceState = 'loading' | 'error' | 'ready'

const props = defineProps<{
  state: WorkspaceState
  errorMessage?: string
  game?: AwdpScreenSnapshot['game']
  stats?: AwdpScreenSnapshot['stats']
  scoreboard: AwdpTeamScore[]
  challenges: AwdpChallengeStatus[]
  recentEvents: AwdpScreenEvent[]
  roundTimeline: AwdpRoundStat[]
  remainingSeconds: number
  connectionStatus: AwdpScreenConnectionStatus
  reconnectAttempts: number
}>()

defineEmits<{
  retry: []
}>()

const timeText = computed(() => {
  const minutes = Math.floor(props.remainingSeconds / 60)
  const seconds = props.remainingSeconds % 60
  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
})

const connectionTone = computed(() => {
  if (props.connectionStatus === 'connected')
    return 'success' as const
  if (props.connectionStatus === 'disconnected')
    return 'danger' as const
  return 'warning' as const
})

const metricItems = computed(() => {
  const stats = props.stats
  if (!stats)
    return []
  return [
    { label: 'Active teams', value: `${stats.activeTeamCount}/${stats.teamCount}`, icon: UsersRound, tone: 'info' },
    { label: 'Challenges', value: `${stats.activeChallengeCount}/${stats.challengeCount}`, icon: Flag, tone: 'info' },
    { label: 'Attack pass', value: `${stats.attackSuccessCount}`, icon: Swords, tone: 'success' },
    { label: 'Defense pass', value: `${stats.defenseSuccessCount}`, icon: ShieldCheck, tone: 'success' },
    { label: 'Attack fail', value: `${stats.attackFailCount}`, icon: Gauge, tone: 'warning' },
    { label: 'Defense fail', value: `${stats.defenseFailCount}`, icon: Activity, tone: 'danger' },
  ]
})
</script>

<template>
  <section v-if="props.state === 'loading'" class="awdp-workspace__state">
    <CommandPanel class="awdp-workspace__skeleton" aria-busy="true">
      <span /><span /><span /><span />
    </CommandPanel>
  </section>

  <section v-else-if="props.state === 'error'" class="awdp-workspace__state">
    <CommandPanel class="awdp-workspace__error" tone="warning">
      <div>
        <CommandSignal label="Telemetry unavailable" tone="danger" />
        <h1>Unable to load the AWDP screen</h1>
        <p>{{ props.errorMessage || 'The public snapshot service did not return usable game data.' }}</p>
      </div>
      <CommandButton label="Retry" @click="$emit('retry')">
        <template #icon>
          <RefreshCw class="size-4" />
        </template>
      </CommandButton>
    </CommandPanel>
  </section>

  <section v-else-if="props.game && props.stats" class="awdp-workspace">
    <header class="awdp-workspace__heading">
      <div class="awdp-workspace__title">
        <CommandSignal :label="`AWDP / ${props.game.status} / ${props.game.phase}`" :tone="connectionTone" />
        <h1>{{ props.game.title }}</h1>
      </div>
      <div class="awdp-workspace__status">
        <CommandSignal
          :label="props.connectionStatus === 'reconnecting' ? `Reconnecting (${props.reconnectAttempts})` : props.connectionStatus"
          :tone="connectionTone"
        />
        <div>
          <span>Round {{ props.game.currentRound }} / {{ props.game.totalRounds }}</span>
          <strong>{{ timeText }}</strong>
        </div>
      </div>
    </header>

    <div class="awdp-workspace__metrics">
      <CommandPanel v-for="metric in metricItems" :key="metric.label" class="awdp-workspace__metric" :tone="metric.tone === 'success' ? 'signal' : metric.tone === 'warning' || metric.tone === 'danger' ? 'warning' : 'primary'">
        <component :is="metric.icon" class="size-4" :class="`awdp-workspace__metric-icon--${metric.tone}`" />
        <div>
          <span>{{ metric.label }}</span>
          <strong>{{ metric.value }}</strong>
        </div>
      </CommandPanel>
    </div>

    <div class="awdp-workspace__primary">
      <CommandAwdpScoreboard :teams="props.scoreboard" />
      <CommandAwdpChallengeMatrix :challenges="props.challenges" />
      <CommandAwdpEventFeed :events="props.recentEvents" />
    </div>

    <CommandAwdpRoundTimeline :rounds="props.roundTimeline" :current-round="props.game.currentRound" />
  </section>
</template>

<style scoped>
.awdp-workspace,
.awdp-workspace__state { display: grid; gap: 12px; }
.awdp-workspace__heading { display: flex; align-items: flex-start; justify-content: space-between; gap: 18px; border-bottom: 1px solid var(--v2-line); padding: 2px 0 13px; }
.awdp-workspace__title h1 { margin: 6px 0 0; color: var(--v2-text); font-size: 24px; font-weight: 680; }
.awdp-workspace__status { display: flex; align-items: center; gap: 12px; }
.awdp-workspace__status > div { display: grid; min-width: 122px; gap: 2px; border-left: 1px solid var(--v2-line); padding-left: 12px; text-align: right; }
.awdp-workspace__status span { color: var(--v2-text-muted); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 10px; }
.awdp-workspace__status strong { color: var(--v2-warning); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 22px; line-height: 1; }
.awdp-workspace__metrics { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 8px; }
.awdp-workspace__metric { display: flex; min-height: 70px; align-items: center; gap: 9px; padding: 11px; }
.awdp-workspace__metric > div { display: grid; min-width: 0; gap: 3px; }
.awdp-workspace__metric span { overflow: hidden; color: var(--v2-text-muted); font-size: 9px; font-weight: 700; letter-spacing: 0.06em; text-overflow: ellipsis; text-transform: uppercase; white-space: nowrap; }
.awdp-workspace__metric strong { color: var(--v2-text); font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 17px; line-height: 1; }
.awdp-workspace__metric-icon--info { color: var(--v2-primary); }
.awdp-workspace__metric-icon--success { color: var(--v2-cyan); }
.awdp-workspace__metric-icon--warning { color: var(--v2-warning); }
.awdp-workspace__metric-icon--danger { color: var(--v2-danger); }
.awdp-workspace__primary { display: grid; grid-template-columns: minmax(250px, 0.82fr) minmax(330px, 1.18fr) minmax(280px, 0.9fr); align-items: stretch; gap: 10px; }
.awdp-workspace__skeleton { display: grid; min-height: 360px; grid-template-rows: 18px 42px 1fr 56px; gap: 16px; padding: 20px; }
.awdp-workspace__skeleton span { display: block; background: var(--v2-surface-hover); animation: awdp-pulse 1.1s ease-in-out infinite alternate; }
.awdp-workspace__skeleton span:nth-child(1) { width: 22%; }
.awdp-workspace__skeleton span:nth-child(2) { width: 48%; }
.awdp-workspace__skeleton span:nth-child(4) { width: 72%; }
.awdp-workspace__error { display: flex; min-height: 240px; align-items: center; justify-content: space-between; gap: 18px; padding: 22px; }
.awdp-workspace__error h1 { margin: 6px 0 0; color: var(--v2-text); font-size: 18px; font-weight: 650; }
.awdp-workspace__error p { max-width: 680px; margin: 7px 0 0; color: var(--v2-text-muted); font-size: 12px; line-height: 1.6; }

@keyframes awdp-pulse { to { opacity: 0.46; } }

@media (max-width: 1200px) {
  .awdp-workspace__metrics { grid-template-columns: repeat(3, minmax(0, 1fr)); }
  .awdp-workspace__primary { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); }
  .awdp-workspace__primary > :last-child { grid-column: 1 / -1; }
}

@media (max-width: 760px) {
  .awdp-workspace__heading,
  .awdp-workspace__error { align-items: flex-start; flex-direction: column; }
  .awdp-workspace__status { width: 100%; justify-content: space-between; }
  .awdp-workspace__primary { grid-template-columns: 1fr; }
  .awdp-workspace__primary > :last-child { grid-column: auto; }
}

@media (max-width: 480px) {
  .awdp-workspace__metrics { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .awdp-workspace__title h1 { font-size: 19px; }
  .awdp-workspace__status { align-items: flex-start; flex-direction: column; gap: 8px; }
  .awdp-workspace__status > div { border-top: 1px solid var(--v2-line); border-left: 0; padding-top: 8px; padding-left: 0; text-align: left; }
}
</style>
