<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloMatch } from './useLiveSoloMatch'
import LiveSoloMatchView from '~/components/views/live-solo/LiveSoloMatchView.vue'
const state = bindViewState(useLiveSoloMatch())
</script>
<template><LiveSoloMatchView :state="state" /></template>
