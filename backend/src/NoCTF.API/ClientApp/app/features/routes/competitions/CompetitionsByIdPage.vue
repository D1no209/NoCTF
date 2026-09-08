<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdPage } from './useCompetitionsByIdPage'
import View from '~/components/views/page/competitions/CompetitionsByIdPageView.vue'

const state = bindViewState(useCompetitionsByIdPage())
await state.initialize()

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
