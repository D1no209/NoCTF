<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useLiveSoloMatchDirections, type LiveSoloMatchDirectionsProps } from './useLiveSoloMatchDirections'
import LiveSoloMatchDirectionsView from '~/components/views/live-solo/LiveSoloMatchDirectionsView.vue'
const props = defineProps<LiveSoloMatchDirectionsProps>()
const emit = defineEmits<{ changed: [] }>()
const state = bindViewState(useLiveSoloMatchDirections(props, () => emit('changed')))
</script>
<template><LiveSoloMatchDirectionsView :state="state" /></template>
