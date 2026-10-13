<script setup lang="ts">
import SpectatorScreenView from '../../components/views/spectator/SpectatorScreenView.vue'
import { bindViewState } from '../shared/view-state'
import { useSpectatorScreen } from './useSpectatorScreen'
const props = defineProps<{ mode: 'Awd' | 'Koh' }>()
const state = bindViewState(useSpectatorScreen(props.mode))
</script>
<template><SpectatorScreenView :state="state" /></template>
