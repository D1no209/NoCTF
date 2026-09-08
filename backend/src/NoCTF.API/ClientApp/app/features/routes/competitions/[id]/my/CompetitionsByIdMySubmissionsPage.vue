<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse } from '~/api'
import { useCompetitionsByIdMySubmissionsPage } from './useCompetitionsByIdMySubmissionsPage'
import View from '~/components/views/page/competitions/[id]/my/CompetitionsByIdMySubmissionsPageView.vue'

const state = bindViewState(useCompetitionsByIdMySubmissionsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
