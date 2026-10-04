<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import type { NoCTFAPIEndpointsGameplayFactsAwdpDefenseProgressResponse, NoCTFAPIEndpointsGameplayFactsPatchVerificationStateResponse } from '~/api/models'
import { useFixSubmit } from './useFixSubmit'
import View from '~/components/views/challenges/FixSubmitView.vue'

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
  defense?: NoCTFAPIEndpointsGameplayFactsAwdpDefenseProgressResponse | NoCTFAPIEndpointsGameplayFactsPatchVerificationStateResponse
  ctfPatchVerification?: boolean
}>()
const emit = defineEmits<{ changed: [], accepted: [] }>()
const state = bindViewState(useFixSubmit(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
