<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionSidebar, type CompetitionSidebarProps } from './useCompetitionSidebar'
import type { CompetitionGroup } from './competition-browser'
import View from '~/components/views/competitions/CompetitionSidebarView.vue'

const props = defineProps<CompetitionSidebarProps>()
const emit = defineEmits<{ 'update:group': [value: CompetitionGroup]; select: [value: string] }>()
const state = bindViewState(useCompetitionSidebar(props, emit))
</script>

<template><View :state="state" /></template>
