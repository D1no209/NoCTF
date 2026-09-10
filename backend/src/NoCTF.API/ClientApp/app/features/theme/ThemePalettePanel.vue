<script setup lang="ts">
import { bindViewState } from '../shared/view-state'
import { useThemePalettePanel } from './useThemePalettePanel'
import View from '../../components/views/theme/ThemePalettePanelView.vue'
const state = bindViewState(useThemePalettePanel())
</script>
<template><View :state="state" /></template>
