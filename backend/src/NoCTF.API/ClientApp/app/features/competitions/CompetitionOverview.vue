<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionOverview } from './useCompetitionOverview'
import View from '~/components/views/competitions/CompetitionOverviewView.vue'

const props = defineProps<{ competition: NoCtfapiEndpointsCompetitionsCompetitionResponse }>()
const state = bindViewState(useCompetitionOverview(props))
</script>

<template><View :state="state" /></template>
