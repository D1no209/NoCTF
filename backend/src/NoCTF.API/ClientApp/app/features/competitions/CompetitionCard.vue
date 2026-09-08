<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'
import { useCompetitionCard } from './useCompetitionCard'
import View from '~/components/views/competitions/CompetitionCardView.vue'

const props = defineProps<{ competition: NoCtfapiEndpointsCompetitionsCompetitionResponse }>()
const state = bindViewState(useCompetitionCard(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
