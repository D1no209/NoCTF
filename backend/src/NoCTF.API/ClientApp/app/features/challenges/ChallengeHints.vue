<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCTFAPIEndpointsChallengesParticipantChallengeHintResponse } from '~/api/models'

import { useChallengeHints } from './useChallengeHints'
import View from '~/components/views/challenges/ChallengeHintsView.vue'

type Hint = NoCTFAPIEndpointsChallengesParticipantChallengeHintResponse

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
  hints?: Hint[] | null
}>()
const emit = defineEmits<{ unlocked: [] }>()
const state = bindViewState(useChallengeHints(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
