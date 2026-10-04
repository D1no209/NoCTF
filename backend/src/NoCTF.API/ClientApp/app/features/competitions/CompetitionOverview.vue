<script setup lang="ts">
import type { NoCTFAPIEndpointsCompetitionsCompetitionResponse } from '~/api/models'
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionOverview } from './useCompetitionOverview'
import View from '~/components/views/competitions/CompetitionOverviewView.vue'

const props = defineProps<{ competition: NoCTFAPIEndpointsCompetitionsCompetitionResponse }>()
const emit = defineEmits<{ audienceChanged: [] }>()
const state = bindViewState(useCompetitionOverview(props, emit))
</script>

<template><View :state="state" /></template>
