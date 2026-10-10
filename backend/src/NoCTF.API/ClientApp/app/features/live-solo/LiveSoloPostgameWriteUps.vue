<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloPostgameWriteUps } from './useLiveSoloPostgameWriteUps'
import LiveSoloPostgameWriteUpsView from '~/components/views/live-solo/LiveSoloPostgameWriteUpsView.vue'
const state=bindViewState(useLiveSoloPostgameWriteUps())
</script>
<template><LiveSoloPostgameWriteUpsView :state="state" /></template>
