<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAwdpScreenData } from '@/features/screen/useAwdpScreenData'
import CommandAwdpScreenWorkspace from '../components/CommandAwdpScreenWorkspace.vue'

const route = useRoute()
const gameId = computed(() => typeof route.params.gameId === 'string' ? route.params.gameId : '')
const forceMock = computed(() =>
  import.meta.env.DEV && (route.query.mock === '1' || route.query.mock === 'true'),
)

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
  reconnectAttempts,
  refreshSnapshot,
} = useAwdpScreenData(gameId, { forceMock })

const workspaceState = computed<'loading' | 'error' | 'ready'>(() => {
  if (isLoading.value && !game.value)
    return 'loading'
  if (error.value && !game.value)
    return 'error'
  return 'ready'
})

const errorMessage = computed(() => error.value instanceof Error ? error.value.message : undefined)
</script>

<template>
  <CommandAwdpScreenWorkspace
    :state="workspaceState"
    :error-message="errorMessage"
    :game="game || undefined"
    :stats="stats || undefined"
    :scoreboard="scoreboard"
    :challenges="challenges"
    :recent-events="recentEvents"
    :round-timeline="roundTimeline"
    :remaining-seconds="remainingSeconds"
    :connection-status="connectionStatus"
    :reconnect-attempts="reconnectAttempts"
    @retry="refreshSnapshot"
  />
</template>
