<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse } from '~/api'

import { useCompetitionsByIdEventsPage } from './useCompetitionsByIdEventsPage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdEventsPageView.vue'

const state = bindViewState(useCompetitionsByIdEventsPage())
await state.initialize()

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
