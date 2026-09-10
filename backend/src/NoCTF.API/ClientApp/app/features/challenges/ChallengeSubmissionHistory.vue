<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useChallengeSubmissionHistory } from './useChallengeSubmissionHistory'
import View from '~/components/views/challenges/ChallengeSubmissionHistoryView.vue'

const props = withDefaults(defineProps<{
  competitionId: string
  competitionChallengeId: string
  refreshKey?: number
}>(), {
  refreshKey: 0,
})
const state = bindViewState(useChallengeSubmissionHistory(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
