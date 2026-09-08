<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdAwdpLivePage } from './useCompetitionsByIdAwdpLivePage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdAwdpLivePageView.vue'

const state = bindViewState(useCompetitionsByIdAwdpLivePage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
