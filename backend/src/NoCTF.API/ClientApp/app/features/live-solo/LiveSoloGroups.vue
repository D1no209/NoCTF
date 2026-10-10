<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloGroups } from './useLiveSoloGroups'
import LiveSoloGroupsView from '~/components/views/live-solo/LiveSoloGroupsView.vue'
const state = bindViewState(useLiveSoloGroups())
</script>
<template><LiveSoloGroupsView :state="state" /></template>
