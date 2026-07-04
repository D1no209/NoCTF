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
  <header class="awdp-panel awdp-header-grid p-3">
    <div class="flex min-w-0 items-center gap-3">
      <div class="flex size-10 shrink-0 items-center justify-center rounded-lg border border-cyan-200/25 bg-cyan-200/10 text-cyan-100">
        <Activity class="size-5" />
      </div>
      <div class="min-w-0">
        <div class="flex flex-wrap items-center gap-2 text-[11px] font-semibold uppercase text-cyan-100/70">
          <span>AWDP command screen</span>
          <span class="rounded border border-cyan-200/20 px-2 py-0.5 text-cyan-50">{{ statusLabel }}</span>
          <span class="rounded border border-slate-300/15 px-2 py-0.5 text-slate-200">{{ phaseLabel }}</span>
        </div>
        <h1 class="mt-1 truncate text-xl font-semibold text-slate-50">
          {{ game.title }}
        </h1>
      </div>
    </div>

    <div class="grid grid-cols-4 gap-2">
      <div class="awdp-header-metric">
        <Clock3 class="size-4 text-cyan-200" />
        <span>Round</span>
        <strong>{{ game.currentRound }} / {{ game.totalRounds }}</strong>
      </div>
      <div class="awdp-header-metric">
        <Users class="size-4 text-cyan-200" />
        <span>Teams</span>
        <strong>{{ stats.teamCount }}</strong>
      </div>
      <div class="awdp-header-metric">
        <Flag class="size-4 text-cyan-200" />
        <span>Challenges</span>
        <strong>{{ stats.challengeCount }}</strong>
      </div>
      <div class="awdp-header-metric">
        <Swords class="size-4 text-cyan-200" />
        <span>Attack</span>
        <strong>{{ stats.totalAttackCount }}</strong>
      </div>
    </div>

    <div class="flex items-center justify-end gap-3">
      <div class="rounded-lg border border-orange-200/20 bg-orange-200/10 px-3 py-1.5 text-right">
        <div class="flex items-center justify-end gap-2 text-[11px] font-semibold uppercase text-orange-100/70">
          <Shield class="size-3.5" />
          Round timer
        </div>
        <div class="font-mono text-2xl font-semibold tabular-nums text-orange-50">
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
  grid-template-columns: minmax(0, 1.2fr) minmax(34rem, 0.85fr) auto;
  gap: 0.75rem;
  align-items: center;
}

.awdp-header-metric {
  display: grid;
  grid-template-columns: auto 1fr;
  grid-template-areas:
    "icon label"
    "icon value";
  align-items: center;
  column-gap: 0.5rem;
  min-height: 3rem;
  border: 1px solid rgb(148 163 184 / 0.16);
  border-radius: 0.5rem;
  background: rgb(15 23 42 / 0.46);
  padding: 0.42rem 0.65rem;
}

.awdp-header-metric svg {
  grid-area: icon;
}

.awdp-header-metric span {
  grid-area: label;
  font-size: 0.68rem;
  font-weight: 700;
  color: rgb(203 213 225 / 0.66);
  text-transform: uppercase;
}

.awdp-header-metric strong {
  grid-area: value;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace;
  color: rgb(248 250 252);
  font-size: 1rem;
}

@media (max-width: 1280px) {
  .awdp-header-grid {
    grid-template-columns: 1fr;
  }
}
</style>
