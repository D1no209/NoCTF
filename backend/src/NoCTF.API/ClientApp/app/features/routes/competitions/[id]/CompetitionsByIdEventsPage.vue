<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdEventsPage } from './useCompetitionsByIdEventsPage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdEventsPageView.vue'

const props = defineProps<{ embedded?: boolean }>()
const state = bindViewState(useCompetitionsByIdEventsPage({ embedded: props.embedded }))
await state.initialize()

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
