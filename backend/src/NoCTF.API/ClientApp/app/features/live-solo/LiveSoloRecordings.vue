<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloRecordings } from './useLiveSoloRecordings'
import LiveSoloRecordingsView from '~/components/views/live-solo/LiveSoloRecordingsView.vue'
const state = bindViewState(useLiveSoloRecordings())
</script>
<template><LiveSoloRecordingsView :state="state" /></template>
