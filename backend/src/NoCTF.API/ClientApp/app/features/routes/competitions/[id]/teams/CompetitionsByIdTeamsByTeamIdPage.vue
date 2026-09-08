<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionsByIdTeamsByTeamIdPage } from './useCompetitionsByIdTeamsByTeamIdPage'
import View from '~/components/views/page/competitions/[id]/teams/CompetitionsByIdTeamsByTeamIdPageView.vue'

const state = bindViewState(useCompetitionsByIdTeamsByTeamIdPage())

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
