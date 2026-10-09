<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloHall } from './useLiveSoloHall'
import LiveSoloHallView from '~/components/views/live-solo/LiveSoloHallView.vue'
const state = bindViewState(useLiveSoloHall())
</script>
<template><LiveSoloHallView :state="state" /></template>
