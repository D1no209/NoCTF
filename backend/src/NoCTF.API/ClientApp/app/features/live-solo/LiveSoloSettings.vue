<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloSettings } from './useLiveSoloSettings'
import LiveSoloSettingsView from '~/components/views/live-solo/LiveSoloSettingsView.vue'
const state = bindViewState(useLiveSoloSettings())
</script>
<template><LiveSoloSettingsView :state="state" /></template>
