<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdNotificationsPage } from './useCompetitionsByIdNotificationsPage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdNotificationsPageView.vue'

const state = bindViewState(useCompetitionsByIdNotificationsPage())
await state.initialize()

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
