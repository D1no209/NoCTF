<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AwdpScreenPage from '@/ui-v1/components/awdp-screen/AwdpScreenPage.vue'

const route = useRoute()
const gameId = computed(() => route.params.gameId as string)
// Mock preview only works in dev builds; production ignores the query param.
const forceMock = computed(() =>
  import.meta.env.DEV && (route.query.mock === '1' || route.query.mock === 'true'),
)
</script>

<template>
  <AwdpScreenPage :game-id="gameId" :force-mock="forceMock" />
</template>
