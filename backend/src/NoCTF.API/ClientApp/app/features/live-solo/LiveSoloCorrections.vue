<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloCorrections } from './useLiveSoloCorrections'
import LiveSoloCorrectionsView from '~/components/views/live-solo/LiveSoloCorrectionsView.vue'
const state = bindViewState(useLiveSoloCorrections())
</script>
<template><LiveSoloCorrectionsView :state="state" /></template>
