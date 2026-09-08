<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdChallengesByCcIdIndexPage } from './useCompetitionsByIdChallengesByCcIdIndexPage'
import View from '~/components/views/page/competitions/[id]/challenges/[ccId]/CompetitionsByIdChallengesByCcIdIndexPageView.vue'

const state = bindViewState(useCompetitionsByIdChallengesByCcIdIndexPage())
await state.initialize()

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
