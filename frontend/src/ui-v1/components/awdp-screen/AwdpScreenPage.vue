<script setup lang="ts">
import { computed } from 'vue'
import AwdpBattleMapPanel from '@/ui-v1/components/awdp-screen/AwdpBattleMapPanel.vue'
import AwdpEventStreamPanel from '@/ui-v1/components/awdp-screen/AwdpEventStreamPanel.vue'
import AwdpRoundTimeline from '@/ui-v1/components/awdp-screen/AwdpRoundTimeline.vue'
import AwdpScoreboardPanel from '@/ui-v1/components/awdp-screen/AwdpScoreboardPanel.vue'
import AwdpScreenErrorState from '@/ui-v1/components/awdp-screen/AwdpScreenErrorState.vue'
import AwdpScreenHeader from '@/ui-v1/components/awdp-screen/AwdpScreenHeader.vue'
import AwdpScreenShell from '@/ui-v1/components/awdp-screen/AwdpScreenShell.vue'
import AwdpScreenSkeleton from '@/ui-v1/components/awdp-screen/AwdpScreenSkeleton.vue'
import AwdpStatsBar from '@/ui-v1/components/awdp-screen/AwdpStatsBar.vue'
import { useAwdpScreenData } from '@/features/screen/useAwdpScreenData'

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
  roundTimeline,
  remainingSeconds,
  isLoading,
  error,
  connectionStatus,
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
  <AwdpScreenShell v-else-if="game && stats">
    <AwdpScreenHeader
      :game="game"
      :stats="stats"
      :remaining-seconds="remainingSeconds"
      :connection-status="connectionStatus"
      :reconnect-attempts="reconnectAttempts"
      :last-sync-at="lastSyncAt"
    />

    <AwdpStatsBar :stats="stats" :current-round="game.currentRound" />

    <div
      class="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(16rem,0.85fr)_1.6fr_minmax(18rem,0.95fr)]"
    >
      <AwdpScoreboardPanel :teams="scoreboard" class="h-full" />
      <AwdpBattleMapPanel
        :teams="scoreboard"
        :challenges="challenges"
        :events="recentEvents"
        :current-round="game.currentRound"
        class="h-full"
      />
      <AwdpEventStreamPanel :events="recentEvents" class="h-full" />
    </div>

    <div class="grid grid-cols-1 gap-4">
      <AwdpRoundTimeline :rounds="roundTimeline" :current-round="game.currentRound" />
    </div>
  </AwdpScreenShell>
</template>
