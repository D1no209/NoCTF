<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdLivePage } from './useCompetitionsByIdLivePage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdLivePageView.vue'

const state = bindViewState(useCompetitionsByIdLivePage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
