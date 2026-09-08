<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdMyTeamPage } from './useCompetitionsByIdMyTeamPage'
import View from '~/components/views/page/competitions/[id]/my/CompetitionsByIdMyTeamPageView.vue'

const state = bindViewState(useCompetitionsByIdMyTeamPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
