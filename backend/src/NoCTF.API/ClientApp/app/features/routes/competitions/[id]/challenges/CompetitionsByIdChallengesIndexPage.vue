<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdChallengesIndexPage } from './useCompetitionsByIdChallengesIndexPage'
import View from '~/components/views/page/competitions/[id]/challenges/CompetitionsByIdChallengesIndexPageView.vue'

const state = bindViewState(useCompetitionsByIdChallengesIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
