<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionOverview } from './useCompetitionOverview'
import View from '~/components/views/competitions/CompetitionOverviewView.vue'

const props = defineProps<{ competition: NoCtfapiEndpointsCompetitionsCompetitionResponse }>()
const emit = defineEmits<{ audienceChanged: [] }>()
const state = bindViewState(useCompetitionOverview(props, emit))
</script>

<template><View :state="state" /></template>
