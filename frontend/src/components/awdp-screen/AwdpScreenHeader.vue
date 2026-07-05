<script setup lang="ts">
import type { AwdpScreenConnectionStatus, AwdpScreenSnapshot } from '@/types/awdpScreen'
import { Activity, Clock3, Flag, Shield, Swords, Users } from 'lucide-vue-next'
import { computed } from 'vue'
import AwdpConnectionStatus from '@/components/awdp-screen/AwdpConnectionStatus.vue'

const props = defineProps<{
  game: AwdpScreenSnapshot['game']
  stats: AwdpScreenSnapshot['stats']
  remainingSeconds: number
  connectionStatus: AwdpScreenConnectionStatus
  usingMock: boolean
  reconnectAttempts: number
  lastSyncAt?: string | null
}>()

const statusLabel = computed(() => ({
  pending: 'Pending',
  running: 'Running',
  paused: 'Paused',
  ended: 'Ended',
}[props.game.status]))

const phaseLabel = computed(() => ({
  waiting: 'Waiting',
  running: 'Round live',
  settling: 'Settling',
  ended: 'Ended',
}[props.game.phase]))

const timeText = computed(() => {
  const minutes = Math.floor(props.remainingSeconds / 60)
  const seconds = props.remainingSeconds % 60
  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`
})
</script>

<template>
  <header class="awdp-panel awdp-header-grid">
    <div class="header-identity">
      <div class="header-mark">
        <Activity class="size-5" />
      </div>
      <div class="min-w-0">
        <div class="header-kicker">
          <span>AWDP LIVE COMMAND</span>
          <span>{{ statusLabel }}</span>
          <span>{{ phaseLabel }}</span>
        </div>
        <h1 class="header-title">
          {{ game.title }}
        </h1>
      </div>
    </div>

    <div class="header-metrics">
      <div class="awdp-header-metric">
        <Clock3 class="size-4 text-sidebar-foreground/75" />
        <span>Round</span>
        <strong>{{ game.currentRound }} / {{ game.totalRounds }}</strong>
      </div>
      <div class="awdp-header-metric">
        <Users class="size-4 text-sidebar-foreground/75" />
        <span>Teams</span>
        <strong>{{ stats.teamCount }}</strong>
      </div>
      <div class="awdp-header-metric">
        <Flag class="size-4 text-sidebar-foreground/75" />
        <span>Challenges</span>
        <strong>{{ stats.challengeCount }}</strong>
      </div>
      <div class="awdp-header-metric">
        <Swords class="size-4 text-sidebar-foreground/75" />
        <span>Break</span>
        <strong>{{ stats.totalAttackCount }}</strong>
      </div>
    </div>

    <div class="header-live">
      <div class="round-clock">
        <div class="round-clock-label">
          <Shield class="size-3.5" />
          Round timer
        </div>
        <div class="round-clock-value">
          {{ timeText }}
        </div>
      </div>
      <AwdpConnectionStatus
        :status="connectionStatus"
        :using-mock="usingMock"
        :reconnect-attempts="reconnectAttempts"
        :last-sync-at="lastSyncAt"
      />
    </div>
  </header>
</template>

<style scoped>
.awdp-header-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(31rem, 0.86fr) auto;
  gap: clamp(0.5rem, 0.7vw, 0.8rem);
  align-items: center;
  min-height: 5.4rem;
  padding: 0.72rem 0.85rem;
}

.header-identity,
.header-live {
  display: flex;
  min-width: 0;
  align-items: center;
}

.header-identity {
  gap: 0.85rem;
}

.header-live {
  justify-content: flex-end;
  gap: 0.65rem;
}

.header-mark {
  display: grid;
  width: 2.85rem;
  height: 2.85rem;
  flex: 0 0 auto;
  place-items: center;
  border: 1px solid var(--awdp-screen-border-strong);
  border-radius: var(--radius-md);
  background: var(--awdp-screen-panel-raised);
  color: var(--awdp-break);
}

.header-kicker {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 58%, transparent);
  font-size: 0.66rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  text-transform: uppercase;
}

.header-kicker span {
  border: 1px solid var(--awdp-screen-border);
  border-radius: var(--radius-sm);
  padding: 0.18rem 0.42rem;
}

.header-kicker span:first-child {
  border-color: color-mix(in oklch, var(--awdp-break) 44%, transparent);
  color: var(--awdp-break);
}

.header-title {
  margin-top: 0.4rem;
  overflow: hidden;
  color: var(--sidebar-foreground);
  font-size: clamp(1.15rem, 1.35vw, 1.55rem);
  font-weight: 800;
  letter-spacing: 0;
  line-height: 1.1;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.header-metrics {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 0.45rem;
}

.awdp-header-metric {
  display: grid;
  grid-template-columns: auto 1fr;
  grid-template-areas:
    "icon label"
    "icon value";
  align-items: center;
  column-gap: 0.5rem;
  min-height: 3.15rem;
  border: 1px solid var(--awdp-screen-border);
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--awdp-screen-panel-raised) 76%, var(--awdp-screen-bg));
  padding: 0.42rem 0.65rem;
}

.awdp-header-metric svg {
  grid-area: icon;
}

.awdp-header-metric span {
  grid-area: label;
  font-size: 0.68rem;
  font-weight: 700;
  color: color-mix(in oklch, var(--sidebar-foreground) 60%, transparent);
}

.awdp-header-metric strong {
  grid-area: value;
  color: var(--sidebar-foreground);
  font-size: 1rem;
  font-variant-numeric: tabular-nums;
}

.round-clock {
  min-width: 8.6rem;
  border: 1px solid color-mix(in oklch, var(--awdp-fix) 36%, var(--awdp-screen-border));
  border-radius: var(--radius-md);
  background: color-mix(in oklch, var(--awdp-screen-panel-raised) 82%, var(--awdp-screen-bg));
  padding: 0.5rem 0.7rem;
  text-align: right;
}

.round-clock-label {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.35rem;
  color: color-mix(in oklch, var(--sidebar-foreground) 62%, transparent);
  font-size: 0.68rem;
  font-weight: 800;
  text-transform: uppercase;
}

.round-clock-value {
  margin-top: 0.12rem;
  color: var(--sidebar-foreground);
  font-size: clamp(1.45rem, 1.85vw, 2.2rem);
  font-weight: 800;
  line-height: 1;
  font-variant-numeric: tabular-nums;
}

@media (max-width: 1280px) {
  .awdp-header-grid {
    grid-template-columns: 1fr;
  }
}
</style>
