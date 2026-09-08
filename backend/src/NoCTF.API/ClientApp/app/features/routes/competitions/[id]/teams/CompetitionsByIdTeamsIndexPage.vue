<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdTeamsIndexPage } from './useCompetitionsByIdTeamsIndexPage'
import View from '~/components/views/page/competitions/[id]/teams/CompetitionsByIdTeamsIndexPageView.vue'

const state = bindViewState(useCompetitionsByIdTeamsIndexPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
