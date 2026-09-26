<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useCompetitionsByIdProgressionPage } from './useCompetitionsByIdProgressionPage'
import View from '~/components/views/page/competitions/[id]/CompetitionsByIdProgressionPageView.vue'

const state = bindViewState(useCompetitionsByIdProgressionPage())
</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
