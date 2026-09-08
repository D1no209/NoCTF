<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useCompetitionChallengeDetail } from './useCompetitionChallengeDetail'
import View from '~/components/views/challenges/CompetitionChallengeDetailView.vue'

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
}>()
const state = bindViewState(useCompetitionChallengeDetail(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
