<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdQuestionsPage } from './useCompetitionsByIdQuestionsPage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdQuestionsPageView.vue'

const state = bindViewState(useCompetitionsByIdQuestionsPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
