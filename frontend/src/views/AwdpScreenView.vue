<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import AwdpScreenPage from '@/components/awdp-screen/AwdpScreenPage.vue'

const route = useRoute()
const gameId = computed(() => route.params.gameId as string)
const forceMock = computed(() => route.query.mock === '1' || route.query.mock === 'true')
</script>

<template>
  <AwdpScreenPage :game-id="gameId" :force-mock="forceMock" />
</template>
