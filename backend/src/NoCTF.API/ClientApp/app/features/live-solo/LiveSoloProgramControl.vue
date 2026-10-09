<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloProgramControl } from './useLiveSoloProgramControl'
import LiveSoloProgramControlView from '~/components/views/live-solo/LiveSoloProgramControlView.vue'
const state=bindViewState(useLiveSoloProgramControl())
</script>
<template><LiveSoloProgramControlView :state="state" /></template>
