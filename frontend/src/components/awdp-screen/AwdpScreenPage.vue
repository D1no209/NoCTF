<script setup lang="ts">
import { computed } from 'vue'
import AwdpAtmosphereCanvas from '@/components/awdp-screen/AwdpAtmosphereCanvas.vue'
import AwdpBattleMapPanel from '@/components/awdp-screen/AwdpBattleMapPanel.vue'
import AwdpChallengeMatrixPanel from '@/components/awdp-screen/AwdpChallengeMatrixPanel.vue'
import AwdpEventStreamPanel from '@/components/awdp-screen/AwdpEventStreamPanel.vue'
import AwdpRoundTimeline from '@/components/awdp-screen/AwdpRoundTimeline.vue'
import AwdpScoreboardPanel from '@/components/awdp-screen/AwdpScoreboardPanel.vue'
import AwdpScreenErrorState from '@/components/awdp-screen/AwdpScreenErrorState.vue'
import AwdpScreenHeader from '@/components/awdp-screen/AwdpScreenHeader.vue'
import AwdpScreenSkeleton from '@/components/awdp-screen/AwdpScreenSkeleton.vue'
import AwdpStatsBar from '@/components/awdp-screen/AwdpStatsBar.vue'
import { useAwdpScreenData } from '@/composables/useAwdpScreenData'

const props = defineProps<{
  gameId: string
  forceMock?: boolean
}>()

const gameIdRef = computed(() => props.gameId)
const forceMockRef = computed(() => props.forceMock ?? false)

const {
  game,
  stats,
  scoreboard,
  challenges,
  recentEvents,
  animationEvents,
  roundTimeline,
  remainingSeconds,
  isLoading,
  error,
  connectionStatus,
  usingMock,
  lastSyncAt,
  reconnectAttempts,
  refreshSnapshot,
} = useAwdpScreenData(gameIdRef, { forceMock: forceMockRef })
</script>

<template>
  <AwdpScreenSkeleton v-if="isLoading && !game" />
  <AwdpScreenErrorState
    v-else-if="error && !game"
    :message="error.message"
    @retry="refreshSnapshot"
  />
  <main v-else-if="game && stats" class="awdp-screen-shell">
    <div class="awdp-screen-bg" />
    <AwdpAtmosphereCanvas
      :events="animationEvents.length ? animationEvents : recentEvents"
      :teams="scoreboard"
      :challenges="challenges"
      :connection-status="connectionStatus"
    />
    <div class="awdp-screen-frame">
      <AwdpScreenHeader
        :game="game"
        :stats="stats"
        :remaining-seconds="remainingSeconds"
        :connection-status="connectionStatus"
        :using-mock="usingMock"
        :reconnect-attempts="reconnectAttempts"
        :last-sync-at="lastSyncAt"
      />

      <AwdpStatsBar :stats="stats" :current-round="game.currentRound" />

      <div class="awdp-screen-main">
        <AwdpScoreboardPanel :teams="scoreboard" />
        <AwdpBattleMapPanel
          :teams="scoreboard"
          :challenges="challenges"
          :events="recentEvents"
          :current-round="game.currentRound"
        />
        <AwdpEventStreamPanel :events="recentEvents" />
      </div>

      <div class="awdp-screen-bottom">
        <AwdpRoundTimeline :rounds="roundTimeline" :current-round="game.currentRound" />
        <AwdpChallengeMatrixPanel :challenges="challenges" />
      </div>
    </div>
  </main>
</template>

<style>
.awdp-screen-shell {
  --awdp-screen-bg: oklch(0.135 0.035 252);
  --awdp-screen-bg-deep: oklch(0.105 0.03 252);
  --awdp-screen-panel: oklch(0.17 0.036 252);
  --awdp-screen-panel-raised: oklch(0.205 0.04 252);
  --awdp-screen-border: oklch(0.78 0.035 240 / 0.16);
  --awdp-screen-border-strong: oklch(0.78 0.035 240 / 0.32);
  --awdp-screen-grid: oklch(0.78 0.035 240 / 0.045);
  --awdp-break: oklch(0.73 0.13 205);
  --awdp-fix: oklch(0.74 0.13 158);
  --awdp-warn: oklch(0.78 0.15 68);
  --awdp-error: oklch(0.67 0.19 24);
  position: relative;
  min-height: 100dvh;
  overflow: hidden;
  display: grid;
  place-items: center;
  padding: min(1.1vw, 0.9rem);
  background: var(--awdp-screen-bg-deep);
  color: var(--sidebar-foreground);
}

.awdp-screen-bg {
  position: absolute;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background:
    linear-gradient(var(--awdp-screen-grid) 1px, transparent 1px),
    linear-gradient(90deg, var(--awdp-screen-grid) 1px, transparent 1px),
    linear-gradient(180deg, var(--awdp-screen-bg), var(--awdp-screen-bg-deep));
  background-size: 48px 48px, 48px 48px, auto;
}

.awdp-screen-frame,
.awdp-screen-frame > * {
  position: relative;
}

.awdp-screen-frame {
  z-index: 1;
  display: grid;
  gap: clamp(0.42rem, 0.62vw, 0.75rem);
  width: min(100%, calc(100vw - 1.8rem), calc((100dvh - 1.8rem) * 16 / 9));
  aspect-ratio: 16 / 9;
  grid-template-rows: auto auto minmax(0, 1fr) minmax(11.5rem, 0.34fr);
  padding: clamp(0.5rem, 0.7vw, 0.85rem);
}

.awdp-screen-main {
  display: grid;
  grid-template-columns: minmax(17rem, 0.78fr) minmax(0, 1.62fr) minmax(18rem, 0.86fr);
  gap: clamp(0.42rem, 0.62vw, 0.75rem);
  min-height: 0;
}

.awdp-screen-bottom {
  display: grid;
  grid-template-columns: minmax(0, 1.45fr) minmax(21rem, 1fr);
  gap: clamp(0.42rem, 0.62vw, 0.75rem);
  min-height: 0;
}

.awdp-panel {
  border: 1px solid var(--awdp-screen-border);
  border-radius: var(--radius-md);
  background: var(--awdp-screen-panel);
  box-shadow: inset 0 1px 0 oklch(0.95 0.01 240 / 0.035);
}

.awdp-skeleton {
  border-radius: var(--radius-md);
  border: 1px solid var(--awdp-screen-border);
  background:
    linear-gradient(90deg, var(--awdp-screen-panel), var(--awdp-screen-panel-raised), var(--awdp-screen-panel));
  background-size: 220% 100%;
  animation: awdp-skeleton 1600ms ease-in-out infinite;
}

@keyframes awdp-skeleton {
  0% {
    background-position: 120% 0;
  }
  100% {
    background-position: -120% 0;
  }
}

@media (max-aspect-ratio: 4 / 3) {
  .awdp-screen-shell {
    overflow: auto;
    place-items: start center;
  }

  .awdp-screen-frame {
    width: min(100%, 1200px);
    min-height: auto;
    grid-template-rows: auto auto auto auto;
    aspect-ratio: auto;
  }

  .awdp-screen-main,
  .awdp-screen-bottom {
    grid-template-columns: 1fr;
  }
}

@media (prefers-reduced-motion: reduce) {
  .awdp-skeleton {
    animation: none;
  }
}
</style>
