<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloProgram } from './useLiveSoloProgram'
import LiveSoloProgramView from '~/components/views/live-solo/LiveSoloProgramView.vue'
const state = bindViewState(useLiveSoloProgram())
</script>
<template><LiveSoloProgramView :state="state" /></template>
